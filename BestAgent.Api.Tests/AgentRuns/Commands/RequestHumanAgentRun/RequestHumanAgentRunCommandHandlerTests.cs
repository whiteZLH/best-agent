using BestAgent.Application;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Commands.RequestHumanAgentRun;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.RequestHumanAgentRun;

public class RequestHumanAgentRunCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentApprovalRepository _agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
    private readonly IToolInvocationRepository _toolInvocationRepo = Substitute.For<IToolInvocationRepository>();
    private readonly IRunOutboxEventRepository _runOutboxEventRepo = Substitute.For<IRunOutboxEventRepository>();
    private readonly IAgentRunEventBus _eventBus = Substitute.For<IAgentRunEventBus>();
    private readonly IMediator _mediator;

    public RequestHumanAgentRunCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentApprovalRepo);
        services.AddSingleton(_toolInvocationRepo);
        services.AddSingleton(_runOutboxEventRepo);
        services.AddSingleton(_eventBus);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldCreatePendingHumanStep_AndMoveRunToWaitingHuman()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "Running",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _runOutboxEventRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L);

        var result = await _mediator.Send(
            new RequestHumanAgentRunCommand(run.RunId, "Need operator review", null, "u-2", "Bob", "operator"));

        Assert.Equal("WaitingHuman", result.Status);
        Assert.Equal(run.RunId, result.RunId);
        Assert.False(string.IsNullOrWhiteSpace(result.WaitToken));

        await _agentStepRepo.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.RunId == run.RunId &&
                step.StepNo == 5 &&
                step.StepType == "human_wait" &&
                step.Status == "Pending" &&
                step.InputPayload == "Need operator review"),
            Arg.Any<CancellationToken>());
        var addedStep = _agentStepRepo.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Single();
        var payload = HumanApprovalPayloadSerializer.Parse(addedStep.DecisionPayload);
        Assert.Equal("human", payload.WaitType);
        Assert.Equal(ApprovalDecisions.Pending, payload.Decision);
        Assert.Equal("Need operator review", payload.Comment);
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(updated =>
                updated.RunId == run.RunId &&
                updated.Status == "WaitingHuman" &&
                updated.CurrentStepNo == 5 &&
                updated.StatusVersion == 3 &&
                !string.IsNullOrWhiteSpace(updated.CurrentWaitToken)),
            Arg.Any<CancellationToken>());
        await _runOutboxEventRepo.Received(1).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.SeqNo == 1 &&
                evt.EventType == "waiting_human" &&
                evt.RunStatus == "WaitingHuman" &&
                evt.Payload != null &&
                evt.Payload.Contains("Need operator review")),
            Arg.Any<CancellationToken>());
        _eventBus.Received(1).Publish(
            Arg.Is<AgentRunEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.EventType == "waiting_human" &&
                evt.Data.StepType == "human_wait" &&
                evt.Data.Status == "Pending" &&
                evt.Data.Output == "Need operator review"));
    }

    [Fact]
    public async Task Handle_FromWaitingTool_ShouldCancelInvocation_AndCaptureReplacementSource()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingTool",
            CurrentStepNo = 4,
            CurrentWaitToken = "tool-wait-1",
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Pending", "{\"city\":\"Shanghai\"}", null, null, now, now) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var pendingInvocation = new ToolInvocation
        {
            InvocationId = "inv-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            ToolName = "weather",
            Mode = "async",
            Status = "Pending",
            InputPayload = "{\"city\":\"Shanghai\"}",
            CallbackToken = "tool-wait-1",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _toolInvocationRepo.GetPendingByRunIdAndStepIdAsync(run.RunId, pendingStep.StepId, Arg.Any<CancellationToken>())
            .Returns(pendingInvocation);
        _runOutboxEventRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L);

        var result = await _mediator.Send(
            new RequestHumanAgentRunCommand(run.RunId, "Need operator review", null, "u-2", "Bob", "operator"));

        Assert.Equal("WaitingHuman", result.Status);
        await _agentStepRepo.Received(1).UpdateAsync(
            Arg.Is<AgentStep>(step =>
                step.StepId == pendingStep.StepId &&
                step.Status == "Cancelled" &&
                step.ErrorPayload == "Superseded by human takeover request."),
            Arg.Any<CancellationToken>());
        await _toolInvocationRepo.Received(1).UpdateAsync(
            Arg.Is<ToolInvocation>(invocation =>
                invocation.InvocationId == "inv-1" &&
                invocation.Status == "Cancelled" &&
                invocation.ErrorPayload == "Superseded by human takeover request."),
            Arg.Any<CancellationToken>());

        var addedHumanStep = _agentStepRepo.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Single();
        var payload = HumanApprovalPayloadSerializer.Parse(addedHumanStep.DecisionPayload);
        Assert.Equal("tool_wait", payload.SourceType);
        Assert.Equal(pendingStep.StepId, payload.SourceStepId);
        Assert.Equal("inv-1", payload.SourceInvocationId);
        Assert.Equal("weather", payload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", payload.SourceToolInput);
        Assert.True(payload.ContinueAsToolResult);
    }

    [Fact]
    public async Task Handle_FromWaitingApproval_ShouldRejectPendingApproval_AndCaptureApprovalSource()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingApproval",
            CurrentStepNo = 4,
            CurrentWaitToken = "approval-wait-1",
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Pending", "{\"city\":\"Shanghai\"}", null, null, now, now) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(
                ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", "internal_write"))
        };
        var approval = new AgentApproval
        {
            ApprovalId = "approval-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            RequestedAction = "weather",
            RiskLevel = "internal_write",
            RequestPayload = "{\"city\":\"Shanghai\"}",
            Decision = ApprovalDecisions.Pending,
            WaitToken = "approval-wait-1",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _agentApprovalRepo.GetByRunIdAndStepIdAsync(run.RunId, pendingStep.StepId, Arg.Any<CancellationToken>())
            .Returns(approval);
        _runOutboxEventRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L);

        var result = await _mediator.Send(
            new RequestHumanAgentRunCommand(run.RunId, "Escalate to operator", null, "u-2", "Bob", "operator"));

        Assert.Equal("WaitingHuman", result.Status);
        await _agentApprovalRepo.Received(1).UpdateAsync(
            Arg.Is<AgentApproval>(item =>
                item.ApprovalId == "approval-1" &&
                item.Decision == ApprovalDecisions.Rejected &&
                item.Comment == "Superseded by human takeover request." &&
                item.ApproverId == "u-2" &&
                item.ApproverName == "Bob" &&
                item.ApproverRole == "operator"),
            Arg.Any<CancellationToken>());

        var addedHumanStep = _agentStepRepo.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Single();
        var payload = HumanApprovalPayloadSerializer.Parse(addedHumanStep.DecisionPayload);
        Assert.Equal("approval_wait", payload.SourceType);
        Assert.Equal(pendingStep.StepId, payload.SourceStepId);
        Assert.Equal("weather", payload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", payload.SourceToolInput);
        Assert.False(payload.ContinueAsToolResult);
    }

    [Fact]
    public async Task Handle_FromWaitingApprovalHandoff_ShouldRejectPendingApproval_AndCaptureHandoffSource()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingApproval",
            CurrentStepNo = 4,
            CurrentWaitToken = "approval-wait-1",
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 4, "handoff", "Pending", "Please help with refund", null, null, now, now) with
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
        var approval = new AgentApproval
        {
            ApprovalId = "approval-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            RequestedAction = "support_agent",
            RiskLevel = "internal_write",
            RequestPayload = "Please help with refund",
            Decision = ApprovalDecisions.Pending,
            WaitToken = "approval-wait-1",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _agentApprovalRepo.GetByRunIdAndStepIdAsync(run.RunId, pendingStep.StepId, Arg.Any<CancellationToken>())
            .Returns(approval);
        _runOutboxEventRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L);

        var result = await _mediator.Send(
            new RequestHumanAgentRunCommand(run.RunId, "Escalate to operator", null, "u-2", "Bob", "operator"));

        Assert.Equal("WaitingHuman", result.Status);
        await _agentApprovalRepo.Received(1).UpdateAsync(
            Arg.Is<AgentApproval>(item =>
                item.ApprovalId == "approval-1" &&
                item.Decision == ApprovalDecisions.Rejected &&
                item.Comment == "Superseded by human takeover request." &&
                item.ApproverId == "u-2" &&
                item.ApproverName == "Bob" &&
                item.ApproverRole == "operator"),
            Arg.Any<CancellationToken>());

        var addedHumanStep = _agentStepRepo.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Single();
        var payload = HumanApprovalPayloadSerializer.Parse(addedHumanStep.DecisionPayload);
        Assert.Equal("approval_wait", payload.SourceType);
        Assert.Equal(pendingStep.StepId, payload.SourceStepId);
        Assert.Equal("support_agent", payload.SourceToolName);
        Assert.Equal("Please help with refund", payload.SourceToolInput);
        Assert.False(payload.ContinueAsToolResult);
    }

    [Fact]
    public async Task Handle_WhenRunStatusIsNotAllowed_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "Completed",
            CurrentStepNo = 4,
            StatusVersion = 2,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(new RequestHumanAgentRunCommand(run.RunId, null, null, null, null, null)));

        Assert.Contains("cannot request human takeover", ex.Message);
        await _agentStepRepo.DidNotReceive().AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithCompletedToolStep_ShouldCreateHumanWaitPayloadPreservingOriginalToolResult()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "Running",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var completedStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Completed", "{\"city\":\"Shanghai\"}", "sunny", null, now, now) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var completedInvocation = new ToolInvocation
        {
            InvocationId = "inv-1",
            RunId = run.RunId,
            StepId = completedStep.StepId,
            ToolName = "weather",
            Mode = "sync",
            Status = "Completed",
            InputPayload = "{\"city\":\"Shanghai\"}",
            OutputPayload = "sunny",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetByStepIdAsync(completedStep.StepId, Arg.Any<CancellationToken>()).Returns(completedStep);
        _agentStepRepo.ListByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns([completedStep]);
        _toolInvocationRepo.ListByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns([completedInvocation]);
        _runOutboxEventRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L);

        var result = await _mediator.Send(
            new RequestHumanAgentRunCommand(run.RunId, "Override the tool output", completedStep.StepId, "u-2", "Bob", "operator"));

        Assert.Equal("WaitingHuman", result.Status);
        await _agentStepRepo.DidNotReceive().UpdateAsync(
            Arg.Is<AgentStep>(step => step.StepId == completedStep.StepId && step.Status == "Cancelled"),
            Arg.Any<CancellationToken>());

        var addedHumanStep = _agentStepRepo.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Single();
        var payload = HumanApprovalPayloadSerializer.Parse(addedHumanStep.DecisionPayload);
        Assert.Equal("tool_result", payload.SourceType);
        Assert.Equal(completedStep.StepId, payload.SourceStepId);
        Assert.Equal("inv-1", payload.SourceInvocationId);
        Assert.Equal("weather", payload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", payload.SourceToolInput);
        Assert.Equal("sunny", payload.SourceToolOutput);
        Assert.Equal("Completed", payload.SourceToolStatus);
        Assert.True(payload.ContinueAsToolResult);
    }

    [Fact]
    public async Task Handle_WithOlderCompletedToolStep_ShouldRejectHumanOverride()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "Running",
            CurrentStepNo = 6,
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var olderCompletedStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Completed", "{\"city\":\"Shanghai\"}", "sunny", null, now, now) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var latestCompletedStep = AgentRunLoop.CreateStep(run.RunId, 6, "tool_call", "Completed", "{\"city\":\"Beijing\"}", "cloudy", null, now, now) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetByStepIdAsync(olderCompletedStep.StepId, Arg.Any<CancellationToken>()).Returns(olderCompletedStep);
        _agentStepRepo.ListByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns([olderCompletedStep, latestCompletedStep]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(
                new RequestHumanAgentRunCommand(
                    run.RunId,
                    "Override an older tool output",
                    olderCompletedStep.StepId,
                    "u-2",
                    "Bob",
                    "operator")));

        Assert.Contains("is not the latest completed tool result", ex.Message);
        await _agentStepRepo.DidNotReceive().AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentRunRepo.DidNotReceive().UpdateAsync(
            Arg.Is<AgentRun>(item => item.Status == "WaitingHuman"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithCompletedToolStepAfterRunAdvanced_ShouldRejectHumanOverride()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "Running",
            CurrentStepNo = 6,
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var completedToolStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Completed", "{\"city\":\"Shanghai\"}", "sunny", null, now, now) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetByStepIdAsync(completedToolStep.StepId, Arg.Any<CancellationToken>()).Returns(completedToolStep);
        _agentStepRepo.ListByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns([completedToolStep]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(
                new RequestHumanAgentRunCommand(
                    run.RunId,
                    "Override a consumed tool output",
                    completedToolStep.StepId,
                    "u-2",
                    "Bob",
                    "operator")));

        Assert.Contains("is no longer the current step", ex.Message);
        await _agentStepRepo.DidNotReceive().AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentRunRepo.DidNotReceive().UpdateAsync(
            Arg.Is<AgentRun>(item => item.Status == "WaitingHuman"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutHumanOperatorIdentity_ShouldThrowForbidden()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "Running",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _mediator.Send(new RequestHumanAgentRunCommand(run.RunId, "Need operator review", null, null, null, null)));

        Assert.Contains("requires an authenticated or explicit operator identity", ex.Message);
        await _agentStepRepo.DidNotReceive().AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenHumanOperatorRoleIsNotAllowed_ShouldThrowForbidden()
    {
        var services = new ServiceCollection();
        services.AddApplication(
            humanTakeoverPolicyOptions: new HumanTakeoverPolicyOptions
            {
                AllowedHumanOperatorRoles = ["operator", "admin"]
            });
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentApprovalRepo);
        services.AddSingleton(_toolInvocationRepo);
        services.AddSingleton(_runOutboxEventRepo);
        services.AddSingleton(_eventBus);
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "Running",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new RequestHumanAgentRunCommand(run.RunId, "Need operator review", null, "u-2", "Bob", "viewer")));

        Assert.Contains("requires one of roles: operator, admin", ex.Message, StringComparison.Ordinal);
        await _agentStepRepo.DidNotReceive().AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
    }
}
