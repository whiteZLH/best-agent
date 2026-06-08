using BestAgent.Application;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.Exceptions;
using BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.AgentDefinitions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.RejectAgentRunStep;

public class RejectAgentRunStepCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IAgentDefinitionRepository _agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
    private readonly IMediator _mediator;

    public RejectAgentRunStepCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_agentDefinitionRepo);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldAllowConfiguredApproverRole()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "internal_write");
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(new ApprovalPolicyOptions
        {
            AllowedApproverRoles = ["security"],
            RoleRequiredSideEffectLevels = ["internal_write"]
        });
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_agentDefinitionRepo);
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await mediator.Send(new RejectAgentRunStepCommand("run-1", pendingStep.StepId, "Denied", "u-1", "Alice", "security"));

        Assert.Equal("Running", result.Status);
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<RejectAgentRunStepMessage>(msg => msg.ApproverRole == "security"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUseBoundDefinitionApprovalPolicy_ForApproverRoleAuthorization()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "internal_write");
        var services = new ServiceCollection();
        services.AddApplication(new ApprovalPolicyOptions
        {
            AllowedApproverRoles = ["admin"],
            RoleRequiredSideEffectLevels = ["internal_write"]
        });
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_agentDefinitionRepo);
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);
        _agentDefinitionRepo.GetByVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(CreateResolvedDefinition("{\"allowedApproverRoles\":[\"security\"],\"roleRequiredSideEffectLevels\":[\"internal_write\"]}"));

        var result = await mediator.Send(new RejectAgentRunStepCommand("run-1", pendingStep.StepId, "Denied", "u-1", "Alice", "security"));

        Assert.Equal("Running", result.Status);
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<RejectAgentRunStepMessage>(msg => msg.ApproverRole == "security"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueueRejectMessage()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingApproval",
            CurrentWaitToken = "token-abc",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "original input",
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Pending", "input", null, null, now, now) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(ApprovalPayloadSerializer.CreatePending("weather", "input", "internal_write"))
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(new RejectAgentRunStepCommand("run-1", pendingStep.StepId, "Denied", "u-1", "Alice", "admin"));

        Assert.Equal("Running", result.Status);
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Running" && r.CurrentWaitToken == string.Empty),
            Arg.Any<CancellationToken>());
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<RejectAgentRunStepMessage>(msg =>
                msg.RunId == "run-1" &&
                msg.StepId == pendingStep.StepId &&
                msg.Comment == "Denied" &&
                msg.ApproverId == "u-1" &&
                msg.ApproverName == "Alice" &&
                msg.ApproverRole == "admin"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueueRejectMessage_ForPendingHandoffApproval()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "handoff",
            "Pending",
            "Please help with refund",
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = HandoffPayloadSerializer.Serialize(
                HandoffPayloadSerializer.CreatePending(
                    "handoff-wait-1",
                    "support_agent",
                    "Please help with refund",
                    "delegate_and_wait",
                    "child-run-1",
                    approvalRequired: true))
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(new RejectAgentRunStepCommand("run-1", pendingStep.StepId, "Denied", "u-1", "Alice", "admin"));

        Assert.Equal("Running", result.Status);
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Running" && r.CurrentWaitToken == string.Empty),
            Arg.Any<CancellationToken>());
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<RejectAgentRunStepMessage>(msg =>
                msg.RunId == "run-1" &&
                msg.StepId == pendingStep.StepId &&
                msg.Comment == "Denied" &&
                msg.ApproverId == "u-1" &&
                msg.ApproverName == "Alice" &&
                msg.ApproverRole == "admin"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowForbidden_WhenApproverIdentityIsMissing()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "read_only");

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _mediator.Send(new RejectAgentRunStepCommand("run-1", pendingStep.StepId, "Denied", null, null, null)));

        Assert.Contains("requires an authenticated or explicit approver identity", ex.Message);
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<RejectAgentRunStepMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowForbidden_WhenWriteApprovalRoleIsMissing()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "internal_write");

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _mediator.Send(new RejectAgentRunStepCommand("run-1", pendingStep.StepId, "Denied", "u-1", "Alice", "viewer")));

        Assert.Contains("requires one of roles", ex.Message);
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<RejectAgentRunStepMessage>(), Arg.Any<CancellationToken>());
    }

    private static AgentRun CreateWaitingApprovalRun(DateTime now)
    {
        return new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingApproval",
            CurrentWaitToken = "token-abc",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "original input",
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };
    }

    private static AgentStep CreatePendingApprovalStep(AgentRun run, DateTime now, string sideEffectLevel)
    {
        return AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Pending", "input", null, null, now, now) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(
                ApprovalPayloadSerializer.CreatePending("weather", "input", sideEffectLevel))
        };
    }

    private static ResolvedAgentDefinition CreateResolvedDefinition(string? approvalPolicy)
    {
        var now = DateTime.UtcNow;
        return new ResolvedAgentDefinition(
            new AgentDefinition
            {
                Id = "def-1",
                Code = "writer",
                Name = "Writer",
                Enabled = true,
                CurrentVersion = 1,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            },
            new AgentDefinitionVersion
            {
                Id = "ver-1",
                AgentDefinitionId = "def-1",
                Version = 1,
                Status = "Published",
                Name = "Writer v1",
                DefaultModel = "gpt-4o",
                SystemPromptTemplate = "You are a writer.",
                AllowedTools = "[\"weather\"]",
                ApprovalPolicy = approvalPolicy,
                MaxTurns = 5,
                MaxCost = 10m,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            });
    }
}
