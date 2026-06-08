using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using BestAgent.Api.Tests.Observability;
using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Runtime;

public class ApprovalTimeoutDispatcherTests
{
    [Fact]
    public async Task DispatchExpiredAsync_ShouldRejectExpiredApproval_AndPublishEvents()
    {
        using var collector = new ActivityTestCollector(AgentTracing.SourceName);
        var now = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var runRepository = Substitute.For<IAgentRunRepository>();
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var eventBus = Substitute.For<IAgentRunEventBus>();
        var agentMetrics = Substitute.For<IAgentMetrics>();
        var approval = CreatePendingApproval(now);
        var run = CreateWaitingApprovalRun();
        var step = CreatePendingApprovalStep(run.RunId, approval.StepId, now);

        approvalRepository.ListExpiredPendingAsync(now, 10, Arg.Any<CancellationToken>())
            .Returns([approval]);
        runRepository.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        stepRepository.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(step);
        outboxRepository.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L);

        using var services = CreateServiceProvider(
            approvalRepository,
            runRepository,
            stepRepository,
            outboxRepository,
            eventBus);
        var dispatcher = new ApprovalTimeoutDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApprovalTimeoutDispatcher>.Instance,
            new ApprovalTimeoutOptions
            {
                TimeoutMinutes = 30,
                BatchSize = 10,
                TimeoutComment = "Approval timed out."
            },
            agentMetrics);

        var processed = await dispatcher.DispatchExpiredAsync(now, CancellationToken.None);

        Assert.Equal(1, processed);
        await approvalRepository.Received(1).UpdateAsync(
            Arg.Is<AgentApproval>(value =>
                value.ApprovalId == approval.ApprovalId &&
                value.Decision == ApprovalDecisions.Rejected &&
                value.Comment == "Approval timed out." &&
                value.ApproverId == "system"),
            Arg.Any<CancellationToken>());
        await stepRepository.Received(1).UpdateAsync(
            Arg.Is<AgentStep>(value =>
                value.StepId == step.StepId &&
                value.Status == "Failed" &&
                value.ErrorPayload == "Approval timed out." &&
                value.DecisionPayload != null &&
                value.DecisionPayload.Contains(ApprovalDecisions.Rejected)),
            Arg.Any<CancellationToken>());
        await runRepository.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(value =>
                value.RunId == run.RunId &&
                value.Status == "TimedOut" &&
                value.CurrentWaitToken == string.Empty &&
                value.InterruptReason == "Approval timed out."),
            Arg.Any<CancellationToken>());
        await outboxRepository.Received(2).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                (evt.EventType == "approval_timed_out" || evt.EventType == "error") &&
                evt.RunStatus == "TimedOut"),
            Arg.Any<CancellationToken>());
        eventBus.Received(1).Publish(Arg.Is<AgentRunEvent>(evt =>
            evt.EventType == "approval_timed_out" &&
            evt.Data.Status == "TimedOut"));
        eventBus.Received(1).Publish(Arg.Is<AgentRunEvent>(evt =>
            evt.EventType == "error" &&
            evt.Data.Status == "TimedOut"));
        agentMetrics.Received(1).RecordApprovalTimedOut(
            "writer",
            "tool_call",
            Arg.Any<TimeSpan>());
        agentMetrics.Received(1).RecordRunCompleted("writer", "TimedOut", 0m);
        var activity = Assert.Single(collector.Activities, value => value.OperationName == AgentTracing.ApprovalActivityName);
        Assert.Equal("timeout", activity.GetTagItem("bestagent.approval_action"));
        Assert.Equal("timedout", activity.GetTagItem("bestagent.status"));
        Assert.Equal("tool_call", activity.GetTagItem("bestagent.step_type"));
    }

    [Fact]
    public async Task DispatchExpiredAsync_ShouldSkipApproval_WhenRunIsNoLongerWaitingApproval()
    {
        var now = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var runRepository = Substitute.For<IAgentRunRepository>();
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var eventBus = Substitute.For<IAgentRunEventBus>();
        var approval = CreatePendingApproval(now);
        var run = CreateWaitingApprovalRun() with { Status = "Completed", CurrentWaitToken = string.Empty };

        approvalRepository.ListExpiredPendingAsync(now, 10, Arg.Any<CancellationToken>())
            .Returns([approval]);
        runRepository.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);

        using var services = CreateServiceProvider(
            approvalRepository,
            runRepository,
            stepRepository,
            outboxRepository,
            eventBus);
        var dispatcher = new ApprovalTimeoutDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApprovalTimeoutDispatcher>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 30, BatchSize = 10 });

        var processed = await dispatcher.DispatchExpiredAsync(now, CancellationToken.None);

        Assert.Equal(0, processed);
        await approvalRepository.DidNotReceive().UpdateAsync(Arg.Any<AgentApproval>(), Arg.Any<CancellationToken>());
        await stepRepository.DidNotReceive().UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await runRepository.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await outboxRepository.DidNotReceive().AddAsync(Arg.Any<RunOutboxEvent>(), Arg.Any<CancellationToken>());
        eventBus.DidNotReceive().Publish(Arg.Any<AgentRunEvent>());
    }

    [Fact]
    public async Task DispatchExpiredAsync_ShouldRejectExpiredHandoffApproval_AndPublishEvents()
    {
        var now = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var runRepository = Substitute.For<IAgentRunRepository>();
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var eventBus = Substitute.For<IAgentRunEventBus>();
        var approval = CreatePendingApproval(now) with
        {
            RequestedAction = "support_agent",
            RequestPayload = "Please help with refund"
        };
        var run = CreateWaitingApprovalRun();
        var step = AgentRunLoop.CreateStep(run.RunId, 4, "handoff", "Pending", "Please help with refund", null, null, now.AddMinutes(-10), now.AddMinutes(-10)) with
        {
            StepId = approval.StepId,
            DecisionPayload = HandoffPayloadSerializer.Serialize(
                HandoffPayloadSerializer.CreatePending(
                    "approval-wait-1",
                    "support_agent",
                    "Please help with refund",
                    "delegate_and_wait",
                    "child-run-1",
                    approvalRequired: true))
        };

        approvalRepository.ListExpiredPendingAsync(now, 10, Arg.Any<CancellationToken>())
            .Returns([approval]);
        runRepository.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        stepRepository.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(step);
        outboxRepository.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L);

        using var services = CreateServiceProvider(
            approvalRepository,
            runRepository,
            stepRepository,
            outboxRepository,
            eventBus);
        var dispatcher = new ApprovalTimeoutDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApprovalTimeoutDispatcher>.Instance,
            new ApprovalTimeoutOptions
            {
                TimeoutMinutes = 30,
                BatchSize = 10,
                TimeoutComment = "Approval timed out."
            });

        var processed = await dispatcher.DispatchExpiredAsync(now, CancellationToken.None);

        Assert.Equal(1, processed);
        await stepRepository.Received(1).UpdateAsync(
            Arg.Is<AgentStep>(value =>
                value.StepId == step.StepId &&
                value.StepType == "handoff" &&
                value.Status == "Failed" &&
                value.ErrorPayload == "Approval timed out." &&
                value.DecisionPayload != null &&
                value.DecisionPayload.Contains(ApprovalDecisions.Rejected) &&
                value.DecisionPayload.Contains("support_agent")),
            Arg.Any<CancellationToken>());
        eventBus.Received(1).Publish(Arg.Is<AgentRunEvent>(evt =>
            evt.EventType == "approval_timed_out" &&
            evt.Data.StepType == "handoff" &&
            evt.Data.Status == "TimedOut"));
        eventBus.Received(1).Publish(Arg.Is<AgentRunEvent>(evt =>
            evt.EventType == "error" &&
            evt.Data.StepType == "handoff" &&
            evt.Data.Status == "TimedOut"));
    }

    [Fact]
    public async Task DispatchExpiredAsync_ShouldEscalateToWaitingHuman_WhenTimeoutActionRequestsHuman()
    {
        var now = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var runRepository = Substitute.For<IAgentRunRepository>();
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var eventBus = Substitute.For<IAgentRunEventBus>();
        var agentMetrics = Substitute.For<IAgentMetrics>();
        var approval = CreatePendingApproval(now);
        var run = CreateWaitingApprovalRun();
        var step = CreatePendingApprovalStep(run.RunId, approval.StepId, now);

        approvalRepository.ListExpiredPendingAsync(now, 10, Arg.Any<CancellationToken>())
            .Returns([approval]);
        runRepository.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        stepRepository.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(step);
        outboxRepository.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L);

        using var services = CreateServiceProvider(
            approvalRepository,
            runRepository,
            stepRepository,
            outboxRepository,
            eventBus);
        var dispatcher = new ApprovalTimeoutDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApprovalTimeoutDispatcher>.Instance,
            new ApprovalTimeoutOptions
            {
                TimeoutMinutes = 30,
                BatchSize = 10,
                TimeoutComment = "Approval timed out.",
                TimeoutAction = "request_human"
            },
            agentMetrics);

        var processed = await dispatcher.DispatchExpiredAsync(now, CancellationToken.None);

        Assert.Equal(1, processed);
        await approvalRepository.Received(1).UpdateAsync(
            Arg.Is<AgentApproval>(value =>
                value.ApprovalId == approval.ApprovalId &&
                value.Decision == ApprovalDecisions.Rejected &&
                value.Comment == "Approval timed out."),
            Arg.Any<CancellationToken>());
        await stepRepository.Received(1).UpdateAsync(
            Arg.Is<AgentStep>(value =>
                value.StepId == step.StepId &&
                value.Status == "Failed" &&
                value.ErrorPayload == "Approval timed out."),
            Arg.Any<CancellationToken>());
        await stepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(value =>
                value.RunId == run.RunId &&
                value.StepNo == 5 &&
                value.StepType == "human_wait" &&
                value.Status == "Pending" &&
                value.DecisionPayload != null),
            Arg.Any<CancellationToken>());
        await runRepository.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(value =>
                value.RunId == run.RunId &&
                value.Status == "WaitingHuman" &&
                value.CurrentStepNo == 5 &&
                !string.IsNullOrWhiteSpace(value.CurrentWaitToken)),
            Arg.Any<CancellationToken>());
        await outboxRepository.Received(2).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                (evt.EventType == "approval_timed_out" || evt.EventType == "waiting_human") &&
                evt.RunStatus == "WaitingHuman"),
            Arg.Any<CancellationToken>());
        eventBus.Received(1).Publish(Arg.Is<AgentRunEvent>(evt =>
            evt.EventType == "approval_timed_out" &&
            evt.RunStatus == "WaitingHuman" &&
            evt.Data.StepType == "tool_call" &&
            evt.Data.Status == "TimedOut"));
        eventBus.Received(1).Publish(Arg.Is<AgentRunEvent>(evt =>
            evt.EventType == "waiting_human" &&
            evt.RunStatus == "WaitingHuman" &&
            evt.Data.StepType == "human_wait" &&
            evt.Data.Status == "Pending"));
        eventBus.DidNotReceive().Publish(Arg.Is<AgentRunEvent>(evt => evt.EventType == "error"));
        var addedHumanStep = stepRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Single(stepValue => stepValue.StepType == "human_wait");
        var payload = HumanApprovalPayloadSerializer.Parse(addedHumanStep.DecisionPayload);
        Assert.Equal("approval_wait", payload.SourceType);
        Assert.Equal(step.StepId, payload.SourceStepId);
        Assert.Equal("weather", payload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", payload.SourceToolInput);
        Assert.Equal("TimedOut", payload.SourceToolStatus);
        Assert.False(payload.ContinueAsToolResult);
        agentMetrics.Received(1).RecordApprovalTimedOut("writer", "tool_call", Arg.Any<TimeSpan>());
        agentMetrics.DidNotReceive().RecordRunCompleted(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>());
    }

    private static ServiceProvider CreateServiceProvider(
        IAgentApprovalRepository approvalRepository,
        IAgentRunRepository runRepository,
        IAgentStepRepository stepRepository,
        IRunOutboxEventRepository outboxRepository,
        IAgentRunEventBus eventBus)
    {
        var services = new ServiceCollection();
        services.AddSingleton(approvalRepository);
        services.AddSingleton(runRepository);
        services.AddSingleton(stepRepository);
        services.AddSingleton(outboxRepository);
        services.AddSingleton(eventBus);
        return services.BuildServiceProvider();
    }

    private static AgentApproval CreatePendingApproval(DateTime now)
    {
        return new AgentApproval
        {
            ApprovalId = "approval-1",
            RunId = "run-1",
            StepId = "step-1",
            RequestedAction = "weather",
            RiskLevel = "internal_write",
            RequestPayload = "{\"city\":\"Shanghai\"}",
            Decision = ApprovalDecisions.Pending,
            WaitToken = "approval-wait-1",
            ExpiresAt = now.AddMinutes(-5),
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now.AddMinutes(-10),
            LastModifyTime = now.AddMinutes(-10)
        };
    }

    private static AgentRun CreateWaitingApprovalRun()
    {
        return new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingApproval",
            CurrentWaitToken = "approval-wait-1",
            CurrentStepNo = 4,
            StatusVersion = 2
        };
    }

    private static AgentStep CreatePendingApprovalStep(string runId, string stepId, DateTime now)
    {
        return AgentRunLoop.CreateStep(runId, 4, "tool_call", "Pending", "{\"city\":\"Shanghai\"}", null, null, now.AddMinutes(-10), now.AddMinutes(-10)) with
        {
            StepId = stepId,
            DecisionPayload = ApprovalPayloadSerializer.Serialize(
                ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", "internal_write"))
        };
    }
}
