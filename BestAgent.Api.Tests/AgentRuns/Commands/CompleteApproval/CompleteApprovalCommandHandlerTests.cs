using BestAgent.Application;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Commands.CompleteApproval;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.AgentDefinitions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.CompleteApproval;

public class CompleteApprovalCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentApprovalRepository _agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IIdempotencyRecordRepository _idempotencyRecordRepo = Substitute.For<IIdempotencyRecordRepository>();
    private readonly IAgentDefinitionRepository _agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
    private readonly IMediator _mediator;

    public CompleteApprovalCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentApprovalRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_idempotencyRecordRepo);
        services.AddSingleton(_agentDefinitionRepo);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ApprovedDecision_ShouldEnqueueApproveMessage()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "internal_write");
        var approval = CreateApproval(run, pendingStep);

        _agentApprovalRepo.GetByApprovalIdAsync("approval-1", Arg.Any<CancellationToken>()).Returns(approval);
        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(
            new CompleteApprovalCommand(run.RunId, "approval-1", "approved", "u-1", "Alice", "admin", "Looks good"));

        Assert.Equal("Running", result.Status);
        Assert.Equal(ApprovalDecisions.Approved, result.Decision);
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(updated =>
                updated.RunId == run.RunId &&
                updated.Status == "Running" &&
                updated.CurrentWaitToken == string.Empty &&
                updated.StatusVersion == 3),
            Arg.Any<CancellationToken>());
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(message =>
                message.RunId == run.RunId &&
                message.StepId == pendingStep.StepId &&
                message.ApproverId == "u-1" &&
                message.ApproverName == "Alice" &&
                message.ApproverRole == "admin" &&
                message.Comment == "Looks good"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ApprovedDecision_ShouldEnqueueApproveMessage_ForPendingHandoffApproval()
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
        var approval = CreateApproval(run, pendingStep) with
        {
            RequestedAction = "support_agent",
            RequestPayload = "Please help with refund"
        };

        _agentApprovalRepo.GetByApprovalIdAsync("approval-1", Arg.Any<CancellationToken>()).Returns(approval);
        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(
            new CompleteApprovalCommand(run.RunId, "approval-1", "approved", "u-1", "Alice", "admin", "Looks good"));

        Assert.Equal("Running", result.Status);
        Assert.Equal(ApprovalDecisions.Approved, result.Decision);
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(message =>
                message.RunId == run.RunId &&
                message.StepId == pendingStep.StepId &&
                message.ApproverId == "u-1" &&
                message.ApproverName == "Alice" &&
                message.ApproverRole == "admin" &&
                message.Comment == "Looks good"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithIdempotencyKey_ShouldStoreRecordAfterEnqueue()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "internal_write");
        var approval = CreateApproval(run, pendingStep);

        _agentApprovalRepo.GetByApprovalIdAsync("approval-1", Arg.Any<CancellationToken>()).Returns(approval);
        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(
            new CompleteApprovalCommand(run.RunId, "approval-1", "approved", "u-1", "Alice", "admin", "Looks good", " callback-1 "));

        Assert.Equal("Running", result.Status);
        await _idempotencyRecordRepo.Received(1).AddAsync(
            Arg.Is<IdempotencyRecord>(record =>
                record.ScopeType == "approval_complete" &&
                record.ScopeKey.Length == 64 &&
                record.RequestHash.Length == 64 &&
                record.TargetId == "approval-1" &&
                record.Status == "completed" &&
                record.ExtraPayload != null &&
                record.ExtraPayload.Contains(ApprovalDecisions.Approved)),
            Arg.Any<CancellationToken>());
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(message => message.RunId == run.RunId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReplayedIdempotencyKey_ShouldReturnStoredResultWithoutEnqueue()
    {
        var storedResult = new CompleteApprovalResult("run-1", "writer", "original input", null, "Running", ApprovalDecisions.Approved);
        var record = CreateIdempotencyRecord(
            "run-1",
            "approval-1",
            "callback-1",
            ApprovalDecisions.Approved,
            "u-1",
            "Alice",
            "admin",
            "Looks good",
            storedResult);

        _idempotencyRecordRepo.GetByScopeAsync("approval_complete", record.ScopeKey, Arg.Any<CancellationToken>())
            .Returns(record);

        var result = await _mediator.Send(
            new CompleteApprovalCommand("run-1", "approval-1", "approved", "u-1", "Alice", "admin", "Looks good", "callback-1"));

        Assert.Equal("Running", result.Status);
        Assert.Equal(ApprovalDecisions.Approved, result.Decision);
        await _agentApprovalRepo.DidNotReceive().GetByApprovalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<AgentRunMessage>(), Arg.Any<CancellationToken>());
        await _idempotencyRecordRepo.DidNotReceive().AddAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReplayedIdempotencyKeyWithDifferentPayload_ShouldThrowConflict()
    {
        var storedResult = new CompleteApprovalResult("run-1", "writer", "original input", null, "Running", ApprovalDecisions.Approved);
        var record = CreateIdempotencyRecord(
            "run-1",
            "approval-1",
            "callback-1",
            ApprovalDecisions.Approved,
            "u-1",
            "Alice",
            "admin",
            "Looks good",
            storedResult);

        _idempotencyRecordRepo.GetByScopeAsync("approval_complete", record.ScopeKey, Arg.Any<CancellationToken>())
            .Returns(record);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(new CompleteApprovalCommand("run-1", "approval-1", "rejected", "u-1", "Alice", "admin", "No", "callback-1")));

        Assert.Contains("already used", ex.Message);
        await _agentApprovalRepo.DidNotReceive().GetByApprovalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<AgentRunMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RejectedDecision_ShouldEnqueueRejectMessage()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "read_only");
        var approval = CreateApproval(run, pendingStep) with { RiskLevel = "read_only" };

        _agentApprovalRepo.GetByApprovalIdAsync("approval-1", Arg.Any<CancellationToken>()).Returns(approval);
        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(
            new CompleteApprovalCommand(run.RunId, "approval-1", "Rejected", "u-1", "Alice", null, "No"));

        Assert.Equal("Running", result.Status);
        Assert.Equal(ApprovalDecisions.Rejected, result.Decision);
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<RejectAgentRunStepMessage>(message =>
                message.RunId == run.RunId &&
                message.StepId == pendingStep.StepId &&
                message.Comment == "No" &&
                message.ApproverId == "u-1" &&
                message.ApproverName == "Alice"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUseBoundDefinitionApprovalPolicy_ForApproverRoleAuthorization()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "internal_write");
        var approval = CreateApproval(run, pendingStep);
        var services = new ServiceCollection();
        services.AddApplication(new ApprovalPolicyOptions
        {
            AllowedApproverRoles = ["admin"],
            RoleRequiredSideEffectLevels = ["internal_write"]
        });
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentApprovalRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_idempotencyRecordRepo);
        services.AddSingleton(_agentDefinitionRepo);
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        _agentApprovalRepo.GetByApprovalIdAsync("approval-1", Arg.Any<CancellationToken>()).Returns(approval);
        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _agentDefinitionRepo.GetByVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(CreateResolvedDefinition("{\"allowedApproverRoles\":[\"security\"],\"roleRequiredSideEffectLevels\":[\"internal_write\"]}"));

        var result = await mediator.Send(
            new CompleteApprovalCommand(run.RunId, "approval-1", "approved", "u-1", "Alice", "security", "Looks good"));

        Assert.Equal("Running", result.Status);
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(message => message.ApproverRole == "security"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUseTenantApprovalPolicy_ForApproverRoleAuthorization()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now) with
        {
            TenantId = "tenant-a"
        };
        var pendingStep = CreatePendingApprovalStep(run, now, "internal_write");
        var approval = CreateApproval(run, pendingStep);
        var services = new ServiceCollection();
        services.AddApplication(
            new ApprovalPolicyOptions
            {
                AllowedApproverRoles = ["admin"],
                RoleRequiredSideEffectLevels = ["internal_write"]
            },
            tenantApprovalPolicyOptions: new TenantApprovalPolicyOptions
            {
                PoliciesByTenantId = new Dictionary<string, ApprovalPolicyOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tenant-a"] = new()
                    {
                        AllowedApproverRoles = ["security"],
                        RoleRequiredSideEffectLevels = ["internal_write"]
                    }
                }
            });
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentApprovalRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_idempotencyRecordRepo);
        services.AddSingleton(_agentDefinitionRepo);
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        _agentApprovalRepo.GetByApprovalIdAsync("approval-1", Arg.Any<CancellationToken>()).Returns(approval);
        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await mediator.Send(
            new CompleteApprovalCommand(run.RunId, "approval-1", "approved", "u-1", "Alice", "security", "Looks good"));

        Assert.Equal("Running", result.Status);
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(message => message.ApproverRole == "security"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyResolvedApproval_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingApprovalRun(now);
        var pendingStep = CreatePendingApprovalStep(run, now, "read_only");
        var approval = CreateApproval(run, pendingStep) with { Decision = ApprovalDecisions.Approved };

        _agentApprovalRepo.GetByApprovalIdAsync("approval-1", Arg.Any<CancellationToken>()).Returns(approval);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(new CompleteApprovalCommand(run.RunId, "approval-1", "Rejected", "u-1", "Alice", null, "No")));

        Assert.Contains("already 'Approved'", ex.Message);
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<AgentRunMessage>(), Arg.Any<CancellationToken>());
    }

    private static AgentRun CreateWaitingApprovalRun(DateTime now)
    {
        return new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingApproval",
            CurrentWaitToken = "approval-wait",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "original input",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
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

    private static AgentApproval CreateApproval(AgentRun run, AgentStep step)
    {
        return new AgentApproval
        {
            ApprovalId = "approval-1",
            RunId = run.RunId,
            StepId = step.StepId,
            RequestedAction = "weather",
            RiskLevel = "internal_write",
            RequestPayload = "input",
            Decision = ApprovalDecisions.Pending,
            WaitToken = "approval-wait",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = run.CreateTime,
            LastModifyTime = run.LastModifyTime
        };
    }

    private static IdempotencyRecord CreateIdempotencyRecord(
        string runId,
        string approvalId,
        string idempotencyKey,
        string decision,
        string? approverId,
        string? approverName,
        string? approverRole,
        string? comment,
        CompleteApprovalResult result)
    {
        var now = DateTime.UtcNow;
        return new IdempotencyRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            ScopeType = "approval_complete",
            ScopeKey = Hash($"{runId}:{approvalId}:{idempotencyKey}"),
            RequestHash = Hash(System.Text.Json.JsonSerializer.Serialize(new
            {
                decision,
                approverId,
                approverName,
                approverRole,
                comment
            })),
            TargetId = approvalId,
            Status = "completed",
            ExtraPayload = System.Text.Json.JsonSerializer.Serialize(result),
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }

    private static string Hash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
