using BestAgent.Application;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.Exceptions;
using BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.AgentDefinitions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.ApproveAgentRunStep;

public class ApproveAgentRunStepCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IAgentDefinitionRepository _agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
    private readonly IMediator _mediator;

    public ApproveAgentRunStepCommandHandlerTests()
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

        var result = await mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId, "u-1", "Alice", "security", "Looks good"));

        Assert.Equal("Running", result.Status);
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(msg => msg.ApproverRole == "security"),
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

        var result = await mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId, "u-1", "Alice", "security", "Looks good"));

        Assert.Equal("Running", result.Status);
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(msg => msg.ApproverRole == "security"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUseStricterParentApprovalPolicy_ForChildRunAuthorization()
    {
        var now = DateTime.UtcNow;
        var parentRun = new AgentRun
        {
            RunId = "parent-run",
            AgentCode = "writer",
            AgentDefinitionVersionId = "parent-ver",
            Status = "WaitingHandoff",
            CurrentWaitToken = "handoff-token",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "parent input",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var run = CreateWaitingApprovalRun(now) with
        {
            RunId = "run-1",
            AgentCode = "support_agent",
            AgentDefinitionVersionId = "child-ver",
            ParentRunId = parentRun.RunId
        };
        var pendingStep = CreatePendingApprovalStep(run, now, "internal_write");
        var parentHandoffStep = AgentRunLoop.CreateStep(
            parentRun.RunId,
            4,
            "handoff",
            "Pending",
            "delegate",
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = HandoffPayloadSerializer.Serialize(
                HandoffPayloadSerializer.CreatePending(
                    "handoff-wait-1",
                    "support_agent",
                    "delegate",
                    "delegate_and_wait",
                    run.RunId))
        };
        var services = new ServiceCollection();
        services.AddApplication(new ApprovalPolicyOptions
        {
            AllowedApproverRoles = ["owner"],
            RoleRequiredSideEffectLevels = ["internal_write"]
        });
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_agentDefinitionRepo);
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>()).Returns(parentRun);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>()).Returns(parentHandoffStep);
        _agentDefinitionRepo.GetByVersionIdAsync("child-ver", Arg.Any<CancellationToken>())
            .Returns(CreateResolvedDefinition("{\"allowedApproverRoles\":[\"security\",\"admin\"],\"roleRequiredSideEffectLevels\":[]}"));
        _agentDefinitionRepo.GetByVersionIdAsync("parent-ver", Arg.Any<CancellationToken>())
            .Returns(CreateResolvedDefinition("{\"allowedApproverRoles\":[\"security\"],\"roleRequiredSideEffectLevels\":[\"internal_write\"]}"));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new ApproveAgentRunStepCommand(run.RunId, pendingStep.StepId, "u-1", "Alice", "admin", "Looks good")));

        Assert.Contains("requires one of roles: security", ex.Message);
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<ApproveAgentRunStepMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueueApproveMessage()
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
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", "internal_write"))
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId, "u-1", "Alice", "admin", "Looks good"));

        Assert.Equal("Running", result.Status);
        Assert.Equal("run-1", result.RunId);
        Assert.Equal("writer", result.AgentCode);

        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Running" && r.CurrentWaitToken == string.Empty && r.StatusVersion == 3),
            Arg.Any<CancellationToken>());

        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(msg =>
                msg.RunId == "run-1" &&
                msg.StepId == pendingStep.StepId &&
                msg.ApproverId == "u-1" &&
                msg.ApproverName == "Alice" &&
                msg.ApproverRole == "admin" &&
                msg.Comment == "Looks good"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueueApproveMessage_ForPendingHandoffApproval()
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

        var result = await _mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId, "u-1", "Alice", "admin", "Looks good"));

        Assert.Equal("Running", result.Status);
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Running" && r.CurrentWaitToken == string.Empty && r.StatusVersion == 3),
            Arg.Any<CancellationToken>());

        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(msg =>
                msg.RunId == "run-1" &&
                msg.StepId == pendingStep.StepId &&
                msg.ApproverId == "u-1" &&
                msg.ApproverName == "Alice" &&
                msg.ApproverRole == "admin" &&
                msg.Comment == "Looks good"),
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
            _mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId, null, null, null, null)));

        Assert.Contains("requires an authenticated or explicit approver identity", ex.Message);
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<ApproveAgentRunStepMessage>(), Arg.Any<CancellationToken>());
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
            _mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId, "u-1", "Alice", "viewer", null)));

        Assert.Contains("requires one of roles", ex.Message);
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<ApproveAgentRunStepMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongStatus_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "Completed",
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);

        var ex = await Assert.ThrowsAsync<Application.Exceptions.ConflictException>(() =>
            _mediator.Send(new ApproveAgentRunStepCommand("run-1", "step-1", null, null, null, null)));

        Assert.Contains("expected 'WaitingApproval'", ex.Message);
    }

    [Fact]
    public async Task Handle_NonApprovalStep_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingApproval",
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Pending", "input", null, null, now, now);

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var ex = await Assert.ThrowsAsync<Application.Exceptions.ConflictException>(() =>
            _mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId, null, null, null, null)));

        Assert.Contains("not waiting for approval", ex.Message);
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
        return AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(
                ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", sideEffectLevel))
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
