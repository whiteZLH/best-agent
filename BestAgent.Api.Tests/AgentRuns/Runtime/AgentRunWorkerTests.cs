using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using BestAgent.Api.Tests.Observability;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Models;
using BestAgent.Application.Observability;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Knowledge;
using BestAgent.Domain.Tools;
using BestAgent.Infrastructure.Runtime;
using BestAgent.Infrastructure.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Runtime;

public class AgentRunWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldCompletePendingStep_ResumeRun_AndPublishDoneEvent()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var agentMetrics = Substitute.For<IAgentMetrics>();
        var run = CreateWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var pendingInvocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            ToolName = "weather",
            Mode = "async",
            Status = "Pending",
            InputPayload = "{\"city\":\"Shanghai\"}",
            CallbackToken = run.CurrentWaitToken,
            StartedAt = DateTime.UtcNow,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L, 3L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            CreateToolInvocationRepository(pendingInvocation));
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 },
            null,
            agentMetrics);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(
            new ResumeAgentRunMessage(
                run.RunId,
                run.CurrentWaitToken,
                """{"status":"succeeded","data":"sunny","meta":{"source":"webhook"}}""",
                "invocation-1"),
            cts.Token);

        var completedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        var completedPendingStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Completed");
        var finalModelStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "model_call", status: "Completed");
        await WaitForEventAsync(eventBus, eventType: "done");

        Assert.Equal("Completed", completedRun.Status);
        Assert.Equal("All done", completedRun.OutputPayload);
        Assert.Equal(run.StatusVersion + 1, completedRun.StatusVersion);

        Assert.Equal("Completed", completedPendingStep.Status);
        Assert.Equal("sunny", completedPendingStep.OutputPayload);
        Assert.Equal(4, completedPendingStep.StepNo);

        Assert.Equal(5, finalModelStep.StepNo);
        Assert.Contains("Original user input:\nhello", finalModelStep.InputPayload);
        Assert.Contains("Tool result:\nsunny", finalModelStep.InputPayload);
        Assert.Equal("final-answer", finalModelStep.OutputPayload);

        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "step" &&
            evt.Data.StepType == "tool_call" &&
            evt.Data.Status == "Completed" &&
            evt.Data.Output == "sunny");
        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "done" &&
            evt.Data.Status == "Completed" &&
            evt.Data.Output == "All done");
        await runtimeMemoryWriter.Received(1).RecordToolResultAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId),
            "weather",
            "{\"city\":\"Shanghai\"}",
            "sunny",
            true,
            true,
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId && value.Status == "Completed"),
            "All done",
            Arg.Any<CancellationToken>());
        await runOutboxRepo.Received(1).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.EventType == "step" &&
                evt.RunStatus == "WaitingTool" &&
                evt.PublishStatus == "pending" &&
                evt.Payload != null &&
                evt.Payload.Contains("sunny")),
            Arg.Any<CancellationToken>());
        await runOutboxRepo.Received(1).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.EventType == "step" &&
                evt.RunStatus == "WaitingTool" &&
                evt.PublishStatus == "pending" &&
                evt.Payload != null &&
                evt.Payload.Contains("final-answer")),
            Arg.Any<CancellationToken>());
        await runOutboxRepo.Received(1).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.EventType == "done" &&
                evt.RunStatus == "Completed" &&
                evt.PublishStatus == "pending" &&
                evt.Payload != null &&
                evt.Payload.Contains("All done")),
            Arg.Any<CancellationToken>());
        await runOutboxRepo.Received(3).MarkPublishedAsync(
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        agentMetrics.Received(1).RecordRunCompleted("writer", "Completed", 0m);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPersistRunTotalCost_WhenModelReportsUsageCost()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun();
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer", Cost: 0.345678m));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);

        var completedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        await WaitForEventAsync(eventBus, eventType: "done");

        Assert.Equal(0.345678m, completedRun.TotalCost);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailResume_WhenRunLevelMaxTurnsAlreadyConsumed()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateWaitingRun() with { MaxTurns = 1 };
        var modelStep = AgentRunLoop.CreateStep(
            run.RunId,
            3,
            "model_call",
            "Completed",
            "hello",
            "tool-decision",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var pendingInvocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            ToolName = "weather",
            Mode = "async",
            Status = "Pending",
            InputPayload = "{\"city\":\"Shanghai\"}",
            CallbackToken = run.CurrentWaitToken,
            StartedAt = DateTime.UtcNow,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentStepRepo.ListByRunIdAsync(run.RunId, Arg.Any<CancellationToken>())
            .Returns([modelStep, pendingStep]);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            CreateToolInvocationRepository(pendingInvocation));
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new ResumeAgentRunMessage(run.RunId, run.CurrentWaitToken, "sunny", "invocation-1"), cts.Token);

        var completedPendingStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Completed");
        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        var failedStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "failed", status: "Failed");
        await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Completed", completedPendingStep.Status);
        Assert.Equal("sunny", completedPendingStep.OutputPayload);
        Assert.Equal("Failed", failedRun.Status);
        Assert.Equal("Max turns exceeded.", failedRun.InterruptReason);
        Assert.Equal("Max turns exceeded.", failedStep.ErrorPayload);
        await modelGateway.DidNotReceive().GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailResume_WhenCompletedToolResultViolatesOutputSchema()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var pendingInvocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            ToolName = "weather",
            Mode = "async",
            Status = "Pending",
            InputPayload = "{\"city\":\"Shanghai\"}",
            CallbackToken = run.CurrentWaitToken,
            StartedAt = DateTime.UtcNow,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };
        var resolvedDefinition = CreateResolvedDefinition();
        toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                ToolName = "weather",
                OutputSchema = "{\"type\":\"object\",\"required\":[\"forecast\"],\"properties\":{\"forecast\":{\"type\":\"string\"}},\"additionalProperties\":false}",
                CompensationPolicy = "{\"mode\":\"manual\"}"
            });

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);

        var invocationRepository = CreateToolInvocationRepository(pendingInvocation);
        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            invocationRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new ResumeAgentRunMessage(run.RunId, run.CurrentWaitToken, "sunny", "invocation-1"), cts.Token);

        var failedPendingStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Failed");
        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHuman");
        var waitingHumanStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "human_wait", status: "Pending");
        var waitingHumanEvent = await WaitForEventAsync(eventBus, eventType: "waiting_human");
        var failedInvocation = await invocationRepository.GetByInvocationIdAsync("invocation-1", CancellationToken.None);

        Assert.Equal("WaitingHuman", waitingRun.Status);
        Assert.False(string.IsNullOrWhiteSpace(waitingRun.CurrentWaitToken));
        Assert.Equal("Failed", failedPendingStep.Status);
        Assert.True(ToolFailurePayloadSerializer.TryParse(failedPendingStep.ErrorPayload, out var pendingFailurePayload));
        Assert.NotNull(pendingFailurePayload);
        Assert.Equal("weather", pendingFailurePayload!.ToolName);
        Assert.Equal("output_validation", pendingFailurePayload.Stage);
        Assert.Contains("Output for tool 'weather'", pendingFailurePayload.Message);
        Assert.NotNull(pendingFailurePayload.Compensation);
        Assert.Equal("manual", pendingFailurePayload.Compensation!.Mode);
        Assert.NotNull(failedInvocation);
        Assert.Equal("Failed", failedInvocation!.Status);
        Assert.True(ToolFailurePayloadSerializer.TryParse(failedInvocation.ErrorPayload, out var invocationFailurePayload));
        Assert.NotNull(invocationFailurePayload);
        Assert.Equal("weather", invocationFailurePayload!.ToolName);
        Assert.Equal("output_validation", invocationFailurePayload.Stage);
        Assert.Contains("Output for tool 'weather'", invocationFailurePayload.Message);
        Assert.NotNull(invocationFailurePayload.Compensation);
        Assert.Equal("manual", invocationFailurePayload.Compensation!.Mode);
        var waitingPayload = HumanApprovalPayloadSerializer.Parse(waitingHumanStep.DecisionPayload);
        Assert.Equal("tool_failure", waitingPayload.SourceType);
        Assert.Equal(pendingStep.StepId, waitingPayload.SourceStepId);
        Assert.Equal("invocation-1", waitingPayload.SourceInvocationId);
        Assert.Equal("weather", waitingPayload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", waitingPayload.SourceToolInput);
        Assert.Equal("Failed", waitingPayload.SourceToolStatus);
        Assert.True(waitingPayload.ContinueAsToolResult);
        Assert.Contains("manual compensation", waitingPayload.Comment, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("waiting_human", waitingHumanEvent.EventType);
        Assert.Equal("human_wait", waitingHumanEvent.Data.StepType);
        await modelGateway.DidNotReceive().GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.DidNotReceive().RecordToolResultAsync(
            Arg.Any<AgentRun>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCreateChildRun_AndResumeParentAfterHandoffCompletes()
    {
        using var collector = new ActivityTestCollector(AgentTracing.SourceName);
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-final"),
                new GenerateTextResult("parent-final"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please help", "delegate_and_wait"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("parent-final")
            .Returns(StepDecision.Respond("Parent answer"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        var childSteps = new List<AgentStep>();
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
                else if (childRun is not null && step.RunId == childRun.RunId)
                {
                    childSteps.Add(step);
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHandoff");
        Assert.Equal("WaitingHandoff", waitingRun.Status);
        Assert.NotNull(childRun);
        Assert.Equal(parentRun.RunId, childRun!.ParentRunId);
        Assert.Equal(parentRun.RunId, childRun.RootRunId);
        Assert.Equal(parentRun.RunId, childRun.DelegatedByRunId);
        Assert.Equal(parentRun.AgentCode, childRun.DelegatedByAgent);
        Assert.Equal(parentRun.TenantId, childRun.TenantId);
        Assert.Equal(parentRun.UserId, childRun.UserId);
        Assert.Equal(parentRun.SessionId, childRun.SessionId);
        Assert.NotNull(parentHandoffStep);
        Assert.Equal("handoff", parentHandoffStep!.StepType);
        Assert.Equal("Completed", parentHandoffStep.Status);

        var completedChildRun = await WaitForChildRunCompletionAsync(agentRunRepo, parentRun.RunId);
        Assert.Equal("Child answer", completedChildRun.OutputPayload);
        var completedParentRun = await WaitForFinalParentCompletionAsync(agentRunRepo, parentRun.RunId);
        Assert.Equal("Parent answer", completedParentRun.OutputPayload);
        Assert.Equal("Completed", completedParentRun.Status);
        Assert.Contains(eventBus.Events, evt => evt.EventType == "waiting_handoff");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "Child answer");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "Parent answer");
        Assert.Contains(
            collector.Activities,
            activity =>
                activity.OperationName == AgentTracing.RunProcessActivityName &&
                Equals(activity.GetTagItem("bestagent.run_id"), parentRun.RunId));
        Assert.Contains(
            collector.Activities,
            activity =>
                activity.OperationName == AgentTracing.HandoffActivityName &&
                Equals(activity.GetTagItem("bestagent.parent_run_id"), parentRun.RunId) &&
                Equals(activity.GetTagItem("bestagent.target_agent"), "support_agent") &&
                Equals(activity.GetTagItem("bestagent.status"), "waiting_handoff"));

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseSummaryOnlyContextScope_WhenCreatingChildRunFromRouteRulePayload()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun() with { InputPayload = "Original user asks for a refund with order #123" };
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "Please handle the refund", "delegate_and_wait"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
            });

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step with
                    {
                        DecisionPayload = HandoffPayloadSerializer.Serialize(
                            HandoffPayloadSerializer.CreatePending(
                                "handoff-wait-1",
                                "support_agent",
                                "Please handle the refund",
                                "delegate_and_wait",
                                "child-run-1",
                                "route-rule-1",
                                "{\"mode\":\"summary_only\"}"))
                    };
                }
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHandoff");
        Assert.NotNull(childRun);
        Assert.Contains("Delegated task summary:", childRun!.InputPayload);
        Assert.Contains("Please handle the refund", childRun.InputPayload);
        Assert.Contains("Parent user request summary:", childRun.InputPayload);
        Assert.Contains("Original user asks for a refund with order #123", childRun.InputPayload);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAutomaticallyRouteFromRouteRule_WhenRoutingPolicyIsHandoffFirst()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var routeRuleRepository = Substitute.For<IRouteRuleRepository>();
        var parentRun = CreateRunningRun() with { InputPayload = "User asks for a refund on order #123" };
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            routingPolicy: "{\"strategy\":\"handoff-first\"}",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[]");

        routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Refund Route",
                    Priority = 10,
                    MatchType = "intent",
                    MatchExpression = "{\"intent\":\"refund\"}",
                    HandoffMode = "route_only",
                    ContextScope = "{\"mode\":\"summary_only\"}",
                    ApprovalRequired = false,
                    Enabled = true
                }
            ]);
        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("child-final"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep is null
                ? []
                : (IReadOnlyList<AgentStep>)[parentHandoffStep]);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            routeRuleRepository: routeRuleRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHandoff");
        Assert.Equal("WaitingHandoff", waitingRun.Status);
        Assert.NotNull(childRun);
        Assert.NotNull(parentHandoffStep);

        var completedChildRun = await WaitForChildRunCompletionAsync(agentRunRepo, parentRun.RunId);
        Assert.Equal("Child answer", completedChildRun.OutputPayload);
        var completedParentRun = await WaitForParentCompletionWithOutputAsync(agentRunRepo, parentRun.RunId, "Child answer");
        Assert.Equal("Completed", completedParentRun.Status);
        Assert.Equal("Child answer", completedParentRun.OutputPayload);
        Assert.Contains(eventBus.Events, evt => evt.EventType == "waiting_handoff");
        await modelGateway.Received(1).GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());
        Assert.DoesNotContain(
            agentStepRepo.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentStep>(),
            step => step.RunId == parentRun.RunId && step.StepType == "model_call");

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotAutomaticallyRoute_WhenAllRouteTermsAreNotFullyMatched()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var routeRuleRepository = Substitute.For<IRouteRuleRepository>();
        var parentRun = CreateRunningRun() with { InputPayload = "User asks for a refund" };
        AgentRun? updatedRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            routingPolicy: "{\"strategy\":\"handoff-first\"}",
            allowedHandoffs: "[\"support_agent\"]");

        routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Refund Order Route",
                    Priority = 10,
                    MatchType = "keyword",
                    MatchExpression = "{\"all\":[\"refund\",\"order\"]}",
                    HandoffMode = "route_only",
                    Enabled = true
                }
            ]);
        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => updatedRun ?? parentRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("Handled by primary agent"));

        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call => updatedRun = call.Arg<AgentRun>());

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            routeRuleRepository: routeRuleRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");

        await modelGateway.Received(1).GenerateTextAsync(
            Arg.Any<GenerateTextRequest>(),
            Arg.Any<CancellationToken>());
        await agentRunRepo.DidNotReceive().AddAsync(
            Arg.Is<AgentRun>(run => run.ParentRunId == parentRun.RunId),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreferContextOverridesOverDefaultInput_WhenCreatingChildRun()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun() with { InputPayload = "User wants a refund and extra account details" };
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(
                StepDecision.Handoff(
                    "support_agent",
                    "Refund summary only",
                    "delegate_and_wait",
                    contextOverrides: "{\"mode\":\"summary_only\"}"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
            });

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHandoff");
        Assert.NotNull(childRun);
        Assert.Contains("Delegated task summary:", childRun!.InputPayload);
        Assert.Contains("Refund summary only", childRun.InputPayload);
        Assert.Contains("Parent user request summary:", childRun.InputPayload);
        Assert.Contains("User wants a refund and extra account details", childRun.InputPayload);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRestrictChildAllowedTools_WhenHandoffToolOverridesArePresent()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedTools: "[\"weather\",\"search\"]",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-tool-decision"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(
                StepDecision.Handoff(
                    "support_agent",
                    "Please help",
                    "delegate_and_wait",
                    toolOverrides: "{\"allowed\":[\"search\"]}"));
        stepDecisionParser.Parse("child-tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var failedChildRun = await WaitForChildRunStatusAsync(agentRunRepo, parentRun.RunId, "Failed");
        var failedParentRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");

        Assert.Contains("Tool 'weather' is not allowed for agent definition 'support_agent'.", failedChildRun.InterruptReason);
        Assert.Contains("Tool 'weather' is not allowed for agent definition 'support_agent'.", failedParentRun.InterruptReason);
        await toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRestrictChildAllowedTools_WhenRouteRuleToolScopeIsPresent()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedTools: "[\"weather\",\"search\"]",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-tool-decision"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "Please help", "delegate_and_wait"));
        stepDecisionParser.Parse("child-tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    var payload = HandoffPayloadSerializer.Parse(step.DecisionPayload);
                    parentHandoffStep = step with
                    {
                        DecisionPayload = HandoffPayloadSerializer.Serialize(payload with
                        {
                            ToolScope = "{\"allowed\":[\"search\"]}"
                        })
                    };
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var failedChildRun = await WaitForChildRunStatusAsync(agentRunRepo, parentRun.RunId, "Failed");
        Assert.Contains("Tool 'weather' is not allowed for agent definition 'support_agent'.", failedChildRun.InterruptReason);
        await toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRestrictChildAllowedHandoffs_ToParentBoundary()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[\"finance_agent\"]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-handoff-decision"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "Please help", "delegate_and_wait"));
        stepDecisionParser.Parse("child-handoff-decision")
            .Returns(StepDecision.Handoff("finance_agent", "Please escalate to finance", "delegate_and_wait"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var failedChildRun = await WaitForChildRunStatusAsync(agentRunRepo, parentRun.RunId, "Failed");
        var failedParentRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");

        Assert.Contains("Handoff target 'finance_agent' is not allowed for agent definition 'support_agent'.", failedChildRun.InterruptReason);
        Assert.Contains("Handoff target 'finance_agent' is not allowed for agent definition 'support_agent'.", failedParentRun.InterruptReason);
        await agentRunRepo.DidNotReceive().AddAsync(
            Arg.Is<AgentRun>(run => run.ParentRunId == failedChildRun.RunId),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRestrictChildKnowledgeSources_WhenRouteRuleKnowledgeScopeIsPresent()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var knowledgeChunkRepository = Substitute.For<IKnowledgeChunkRepository>();
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var runtimeContextComposer = new RuntimeContextComposer(
            summaryMemoryRepository,
            knowledgeChunkRepository,
            sessionMemoryRepository,
            userMemoryRepository,
            NullAgentMetrics.Instance);
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;
        GenerateTextRequest? childModelRequest = null;
        var modelCallCount = 0;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            memoryPolicy: "{\"includeKnowledge\":true,\"includeSummary\":false,\"includeSessionMemory\":false,\"includeUserMemory\":false,\"maxKnowledgeChunks\":3,\"knowledgeCandidateCount\":3}",
            knowledgeSources: "[\"faq\",\"travel-guide\"]",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                modelCallCount++;
                var request = call.Arg<GenerateTextRequest>();
                if (modelCallCount == 1)
                {
                    return new GenerateTextResult("handoff-decision");
                }

                if (modelCallCount == 2)
                {
                    childModelRequest = request;
                    return new GenerateTextResult("child-final");
                }

                return new GenerateTextResult("parent-final");
            });
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "Please help with policy details", "delegate_and_wait"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("parent-final")
            .Returns(StepDecision.Respond("Parent answer"));

        knowledgeChunkRepository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var sourceCodes = call.Arg<IReadOnlyList<string>>();
                var selectedSource = sourceCodes.Single();
                return (IReadOnlyList<KnowledgeChunk>)
                [
                    new KnowledgeChunk
                    {
                        Id = "chunk-1",
                        TenantId = "tenant-1",
                        DocumentId = "doc-1",
                        ChunkNo = 1,
                        Content = $"Knowledge from {selectedSource}",
                        TokenCount = 12,
                        Source = selectedSource,
                        Metadata = "{}",
                        CreateTime = DateTime.UtcNow,
                        LastModifyTime = DateTime.UtcNow,
                        Creator = "system",
                        CreatorName = "system",
                        LastModifier = "system",
                        LastModifierName = "system"
                    }
                ];
            });

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    var payload = HandoffPayloadSerializer.Parse(step.DecisionPayload);
                    parentHandoffStep = step with
                    {
                        DecisionPayload = HandoffPayloadSerializer.Serialize(payload with
                        {
                            KnowledgeScope = "{\"allowed\":[\"faq\",\"non-existent\"]}"
                        })
                    };
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            runtimeContextComposer: runtimeContextComposer);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        await WaitForChildRunStatusAsync(agentRunRepo, parentRun.RunId, "Completed");

        Assert.NotNull(childModelRequest);
        Assert.Contains("Knowledge from faq", childModelRequest!.Input);
        Assert.DoesNotContain("Knowledge from travel-guide", childModelRequest.Input);
        await knowledgeChunkRepository.Received(1).ListByKnowledgeSourceCodesAsync(
            "tenant-1",
            Arg.Is<IReadOnlyList<string>>(sources =>
                sources.Count == 1 &&
                string.Equals(sources[0], "faq", StringComparison.Ordinal)),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRestrictChildKnowledgeSources_WhenKnowledgeScopeUsesSourcesField()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var knowledgeChunkRepository = Substitute.For<IKnowledgeChunkRepository>();
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var runtimeContextComposer = new RuntimeContextComposer(
            summaryMemoryRepository,
            knowledgeChunkRepository,
            sessionMemoryRepository,
            userMemoryRepository,
            NullAgentMetrics.Instance);
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;
        GenerateTextRequest? childModelRequest = null;
        var modelCallCount = 0;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            memoryPolicy: "{\"includeKnowledge\":true,\"includeSummary\":false,\"includeSessionMemory\":false,\"includeUserMemory\":false,\"maxKnowledgeChunks\":3,\"knowledgeCandidateCount\":3}",
            knowledgeSources: "[\"faq\",\"travel-guide\"]",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                modelCallCount++;
                var request = call.Arg<GenerateTextRequest>();
                if (modelCallCount == 1)
                {
                    return new GenerateTextResult("handoff-decision");
                }

                if (modelCallCount == 2)
                {
                    childModelRequest = request;
                    return new GenerateTextResult("child-final");
                }

                return new GenerateTextResult("parent-final");
            });
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "Please help with policy details", "delegate_and_wait"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("parent-final")
            .Returns(StepDecision.Respond("Parent answer"));

        knowledgeChunkRepository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var sourceCodes = call.Arg<IReadOnlyList<string>>();
                var selectedSource = sourceCodes.Single();
                return (IReadOnlyList<KnowledgeChunk>)
                [
                    new KnowledgeChunk
                    {
                        Id = "chunk-1",
                        TenantId = "tenant-1",
                        DocumentId = "doc-1",
                        ChunkNo = 1,
                        Content = $"Knowledge from {selectedSource}",
                        TokenCount = 12,
                        Source = selectedSource,
                        Metadata = "{}",
                        CreateTime = DateTime.UtcNow,
                        LastModifyTime = DateTime.UtcNow,
                        Creator = "system",
                        CreatorName = "system",
                        LastModifier = "system",
                        LastModifierName = "system"
                    }
                ];
            });

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    var payload = HandoffPayloadSerializer.Parse(step.DecisionPayload);
                    parentHandoffStep = step with
                    {
                        DecisionPayload = HandoffPayloadSerializer.Serialize(payload with
                        {
                            KnowledgeScope = "{\"sources\":[\"faq\",\"non-existent\"]}"
                        })
                    };
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            runtimeContextComposer: runtimeContextComposer);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        await WaitForChildRunStatusAsync(agentRunRepo, parentRun.RunId, "Completed");

        Assert.NotNull(childModelRequest);
        Assert.Contains("Knowledge from faq", childModelRequest!.Input);
        Assert.DoesNotContain("Knowledge from travel-guide", childModelRequest.Input);
        await knowledgeChunkRepository.Received(1).ListByKnowledgeSourceCodesAsync(
            "tenant-1",
            Arg.Is<IReadOnlyList<string>>(sources =>
                sources.Count == 1 &&
                string.Equals(sources[0], "faq", StringComparison.Ordinal)),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSuppressChildMemoryContext_WhenContextModeIsSummaryOnly()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var knowledgeChunkRepository = Substitute.For<IKnowledgeChunkRepository>();
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var runtimeContextComposer = new RuntimeContextComposer(
            summaryMemoryRepository,
            knowledgeChunkRepository,
            sessionMemoryRepository,
            userMemoryRepository,
            NullAgentMetrics.Instance);
        var parentRun = CreateRunningRun() with { InputPayload = "User wants a refund and extra account details" };
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;
        GenerateTextRequest? childModelRequest = null;
        var modelCallCount = 0;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            memoryPolicy: "{\"includeKnowledge\":true,\"includeSummary\":true,\"includeSessionMemory\":true,\"maxSessionMemories\":2,\"includeUserMemory\":true,\"maxUserMemories\":2,\"maxKnowledgeChunks\":2,\"knowledgeCandidateCount\":4}",
            knowledgeSources: "[\"faq\"]",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                modelCallCount++;
                var request = call.Arg<GenerateTextRequest>();
                if (modelCallCount == 1)
                {
                    return new GenerateTextResult("handoff-decision");
                }

                if (modelCallCount == 2)
                {
                    childModelRequest = request;
                    return new GenerateTextResult("child-final");
                }

                return new GenerateTextResult("parent-final");
            });
        stepDecisionParser.Parse("handoff-decision")
            .Returns(
                StepDecision.Handoff(
                    "support_agent",
                    "Refund summary only",
                    "delegate_and_wait",
                    contextOverrides: "{\"mode\":\"summary_only\"}"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("parent-final")
            .Returns(StepDecision.Respond("Parent answer"));

        summaryMemoryRepository.GetLatestActiveAsync("tenant-1", "session-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SummaryMemory
            {
                Id = "summary-1",
                TenantId = "tenant-1",
                RunId = "child-run",
                SessionId = "session-1",
                SummaryText = "Conversation summary should not appear."
            });
        sessionMemoryRepository.ListActiveBySessionAsync("tenant-1", "session-1", 2, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SessionMemory
                {
                    Id = "session-memory-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    RunId = "run-001",
                    ContentJson = "{\"lastTool\":\"weather\"}"
                }
            ]);
        userMemoryRepository.ListActiveByUserAsync("tenant-1", "user-1", 2, Arg.Any<CancellationToken>())
            .Returns(
            [
                new UserMemory
                {
                    Id = "user-memory-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    MemoryKey = "seat_preference",
                    MemoryType = "preference",
                    MemoryValue = "\"aisle\""
                }
            ]);
        knowledgeChunkRepository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new KnowledgeChunk
                {
                    Id = "chunk-1",
                    TenantId = "tenant-1",
                    DocumentId = "doc-1",
                    ChunkNo = 1,
                    Content = "Knowledge from faq",
                    Source = "faq"
                }
            ]);

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            runtimeContextComposer: runtimeContextComposer);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        await WaitForChildRunStatusAsync(agentRunRepo, parentRun.RunId, "Completed");

        Assert.NotNull(childModelRequest);
        Assert.Contains("Delegated task summary:", childModelRequest!.Input);
        Assert.Contains("Refund summary only", childModelRequest.Input);
        Assert.Contains("Parent user request summary:", childModelRequest.Input);
        Assert.DoesNotContain("Conversation summary:", childModelRequest.Input);
        Assert.DoesNotContain("Session memory:", childModelRequest.Input);
        Assert.DoesNotContain("User memory:", childModelRequest.Input);
        Assert.DoesNotContain("Reference knowledge:", childModelRequest.Input);
        await knowledgeChunkRepository.DidNotReceive().ListByKnowledgeSourceCodesAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepChildMemoryReads_ButDisableWrites_WhenMemoryScopeIsReadOnly()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var knowledgeChunkRepository = Substitute.For<IKnowledgeChunkRepository>();
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var routeRuleRepository = Substitute.For<IRouteRuleRepository>();
        var runtimeContextComposer = new RuntimeContextComposer(
            summaryMemoryRepository,
            knowledgeChunkRepository,
            sessionMemoryRepository,
            userMemoryRepository,
            NullAgentMetrics.Instance);
        var parentRun = CreateRunningRun() with { InputPayload = "User asks for a refund on order #123" };
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;
        GenerateTextRequest? childModelRequest = null;
        var modelCallCount = 0;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            routingPolicy: "{\"strategy\":\"handoff-first\"}",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            memoryPolicy: "{\"toolResultMemoryEnabled\":true,\"userMemoryWriteEnabled\":true,\"summaryMemoryWriteEnabled\":true,\"includeKnowledge\":true,\"includeSummary\":true,\"includeSessionMemory\":true,\"includeUserMemory\":true,\"maxKnowledgeChunks\":2,\"knowledgeCandidateCount\":4,\"maxSessionMemories\":2,\"maxUserMemories\":2}",
            knowledgeSources: "[\"faq\"]",
            allowedHandoffs: "[]");

        routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Refund Route",
                    Priority = 10,
                    MatchType = "intent",
                    MatchExpression = "{\"intent\":\"refund\"}",
                    HandoffMode = "delegate_and_wait",
                    MemoryScope = "{\"mode\":\"read_only\"}",
                    Enabled = true
                }
            ]);
        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                modelCallCount++;
                var request = call.Arg<GenerateTextRequest>();
                if (modelCallCount == 1)
                {
                    childModelRequest = request;
                    return new GenerateTextResult("child-tool-decision");
                }

                if (modelCallCount == 2)
                {
                    return new GenerateTextResult("child-final");
                }

                return new GenerateTextResult("parent-final");
            });
        stepDecisionParser.Parse("child-tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("parent-final")
            .Returns(StepDecision.Respond("Parent answer"));
        toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "sunny"));

        summaryMemoryRepository.GetLatestActiveAsync("tenant-1", "session-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SummaryMemory
            {
                Id = "summary-1",
                TenantId = "tenant-1",
                RunId = "run-001",
                SessionId = "session-1",
                SummaryText = "Prior summary should still be visible."
            });
        sessionMemoryRepository.ListActiveBySessionAsync("tenant-1", "session-1", 2, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SessionMemory
                {
                    Id = "session-memory-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    RunId = "run-001",
                    ContentJson = "{\"lastTool\":\"weather\"}"
                }
            ]);
        userMemoryRepository.ListActiveByUserAsync("tenant-1", "user-1", 2, Arg.Any<CancellationToken>())
            .Returns(
            [
                new UserMemory
                {
                    Id = "user-memory-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    MemoryKey = "refund_preference",
                    MemoryType = "preference",
                    MemoryValue = "\"email\""
                }
            ]);
        knowledgeChunkRepository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new KnowledgeChunk
                {
                    Id = "chunk-1",
                    TenantId = "tenant-1",
                    DocumentId = "doc-1",
                    ChunkNo = 1,
                    Content = "Knowledge from faq",
                    TokenCount = 12,
                    Source = "faq",
                    Metadata = "{}",
                    CreateTime = DateTime.UtcNow,
                    LastModifyTime = DateTime.UtcNow,
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system"
                }
            ]);

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep is null
                ? []
                : (IReadOnlyList<AgentStep>)
                [
                    AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow),
                    parentHandoffStep
                ]);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            routeRuleRepository: routeRuleRepository,
            runtimeContextComposer: runtimeContextComposer);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var completedChildRun = await WaitForChildRunCompletionAsync(agentRunRepo, parentRun.RunId);
        var completedParentRun = await WaitForFinalParentCompletionAsync(agentRunRepo, parentRun.RunId);

        Assert.NotNull(childModelRequest);
        Assert.Contains("Prior summary should still be visible.", childModelRequest!.Input);
        Assert.Contains("Knowledge from faq", childModelRequest.Input);
        Assert.Contains("{\"lastTool\":\"weather\"}", childModelRequest.Input);
        Assert.Contains("\"email\"", childModelRequest.Input);
        Assert.Contains("Child answer", completedChildRun.OutputPayload);
        Assert.Equal("Parent answer", completedParentRun.OutputPayload);
        await runtimeMemoryWriter.DidNotReceive().RecordToolResultAsync(
            Arg.Is<AgentRun>(run => run.ParentRunId == parentRun.RunId),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.DidNotReceive().RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(run => run.ParentRunId == parentRun.RunId),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(run => run.RunId == parentRun.RunId && run.Status == "Completed"),
            "Parent answer",
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotWriteChildToolResultMemory_WhenContextModeIsSummaryOnly()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            memoryPolicy: "{\"toolResultMemoryEnabled\":true,\"userMemoryWriteEnabled\":true,\"summaryMemoryWriteEnabled\":true}",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-tool-decision"),
                new GenerateTextResult("child-final"),
                new GenerateTextResult("parent-final"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(
                StepDecision.Handoff(
                    "support_agent",
                    "Please help",
                    "delegate_and_wait",
                    contextOverrides: "{\"mode\":\"summary_only\"}"));
        stepDecisionParser.Parse("child-tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("parent-final")
            .Returns(StepDecision.Respond("Parent answer"));
        toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "sunny"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep is null
                ? []
                : (IReadOnlyList<AgentStep>)
                [
                    AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow),
                    parentHandoffStep
                ]);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var completedChildRun = await WaitForChildRunCompletionAsync(agentRunRepo, parentRun.RunId);
        var completedParentRun = await WaitForFinalParentCompletionAsync(agentRunRepo, parentRun.RunId);

        Assert.Equal("Child answer", completedChildRun.OutputPayload);
        Assert.Equal("Parent answer", completedParentRun.OutputPayload);
        await runtimeMemoryWriter.DidNotReceive().RecordToolResultAsync(
            Arg.Is<AgentRun>(run => run.ParentRunId == parentRun.RunId),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(run => run.RunId == parentRun.RunId && run.Status == "Completed"),
            "Parent answer",
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotWriteChildSummaryMemory_WhenContextModeIsSummaryOnly()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            memoryPolicy: "{\"summaryMemoryWriteEnabled\":true}",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-final"),
                new GenerateTextResult("parent-final"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(
                StepDecision.Handoff(
                    "support_agent",
                    "Please help",
                    "delegate_and_wait",
                    contextOverrides: "{\"mode\":\"summary_only\"}"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("parent-final")
            .Returns(StepDecision.Respond("Parent answer"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep is null
                ? []
                : (IReadOnlyList<AgentStep>)
                [
                    AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow),
                    parentHandoffStep
                ]);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var completedChildRun = await WaitForChildRunCompletionAsync(agentRunRepo, parentRun.RunId);
        var completedParentRun = await WaitForFinalParentCompletionAsync(agentRunRepo, parentRun.RunId);

        Assert.Equal("Child answer", completedChildRun.OutputPayload);
        Assert.Equal("Parent answer", completedParentRun.OutputPayload);
        await runtimeMemoryWriter.DidNotReceive().RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(run => run.ParentRunId == parentRun.RunId),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(run => run.RunId == parentRun.RunId && run.Status == "Completed"),
            "Parent answer",
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCreateChildRun_AndCompleteParentDirectly_ForRouteOnlyHandoff()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-final"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please help", "route_only"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHandoff");
        Assert.Equal("WaitingHandoff", waitingRun.Status);
        Assert.NotNull(childRun);
        Assert.Equal(parentRun.RunId, childRun!.ParentRunId);
        Assert.NotNull(parentHandoffStep);

        var completedChildRun = await WaitForChildRunCompletionAsync(agentRunRepo, parentRun.RunId);
        Assert.Equal("Child answer", completedChildRun.OutputPayload);
        var completedParentRun = await WaitForParentCompletionWithOutputAsync(agentRunRepo, parentRun.RunId, "Child answer");
        Assert.Equal("Completed", completedParentRun.Status);
        Assert.Equal("Child answer", completedParentRun.OutputPayload);
        Assert.Equal(string.Empty, completedParentRun.CurrentWaitToken);
        Assert.Equal("Completed", parentHandoffStep!.Status);
        Assert.Equal("Child answer", parentHandoffStep.OutputPayload);
        Assert.Contains("\"Mode\":\"route_only\"", parentHandoffStep.DecisionPayload);
        Assert.Contains(eventBus.Events, evt => evt.EventType == "waiting_handoff");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "Child answer");
        Assert.DoesNotContain(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "Parent answer");
        await modelGateway.Received(2).GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(value => value.RunId == parentRun.RunId && value.Status == "Completed"),
            "Child answer",
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCreateChildRun_AndMergeChildResult_ForDelegateAndMergeHandoff()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-final"),
                new GenerateTextResult("merge-final"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please help", "delegate_and_merge"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("merge-final")
            .Returns(StepDecision.Respond("Merged parent answer"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHandoff");
        Assert.Equal("WaitingHandoff", waitingRun.Status);
        Assert.NotNull(childRun);
        Assert.NotNull(parentHandoffStep);

        var completedChildRun = await WaitForChildRunCompletionAsync(agentRunRepo, parentRun.RunId);
        Assert.Equal("Child answer", completedChildRun.OutputPayload);
        var completedParentRun = await WaitForParentCompletionWithOutputAsync(agentRunRepo, parentRun.RunId, "Merged parent answer");
        Assert.Equal("Completed", completedParentRun.Status);
        Assert.Equal("Merged parent answer", completedParentRun.OutputPayload);
        Assert.Equal("Completed", parentHandoffStep!.Status);
        Assert.Equal("Child answer", parentHandoffStep.OutputPayload);
        Assert.Contains("\"Mode\":\"delegate_and_merge\"", parentHandoffStep.DecisionPayload);
        Assert.Contains(eventBus.Events, evt => evt.EventType == "waiting_handoff");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "Child answer");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "Merged parent answer");
        await modelGateway.Received(3).GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());
        await modelGateway.Received(1).GenerateTextAsync(
            Arg.Is<GenerateTextRequest>(request =>
                request.Input.Contains("Handoff result to merge:\nChild answer") &&
                request.Input.Contains("Merge the child result into a final user-facing answer.")),
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(value => value.RunId == parentRun.RunId && value.Status == "Completed"),
            "Merged parent answer",
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailHandoff_WhenDefaultMaxHandoffDepthIsExceeded()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun() with
        {
            ParentRunId = "ancestor-3",
            RootRunId = "ancestor-root",
            DelegatedByRunId = "ancestor-3",
            DelegatedByAgent = "router"
        };
        var allRuns = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        allRuns[parentRun.RunId] = parentRun;
        allRuns["ancestor-3"] = new AgentRun { RunId = "ancestor-3", ParentRunId = "ancestor-2" };
        allRuns["ancestor-2"] = new AgentRun { RunId = "ancestor-2", ParentRunId = "ancestor-1" };
        allRuns["ancestor-1"] = new AgentRun { RunId = "ancestor-1", ParentRunId = string.Empty };
        AgentRun? latestParentRun = parentRun;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var runId = call.Arg<string>();
                if (runId == parentRun.RunId)
                {
                    return latestParentRun;
                }

                return allRuns.TryGetValue(runId, out var run) ? run : null;
            });
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please help", "delegate_and_wait"));

        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    latestParentRun = updated;
                    allRuns[updated.RunId] = updated;
                }
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        var failedStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "failed", status: "Failed");
        var errorEvent = await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Failed", failedRun.Status);
        Assert.Contains("Handoff depth 4 exceeds the configured maximum 3", failedRun.InterruptReason);
        Assert.Contains("Handoff depth 4 exceeds the configured maximum 3", failedStep.ErrorPayload);
        Assert.Equal("handoff", errorEvent.Data.StepType);
        Assert.Contains("Handoff depth 4 exceeds the configured maximum 3", errorEvent.Data.Error);
        await agentRunRepo.DidNotReceive().AddAsync(
            Arg.Is<AgentRun>(run => run.RunId != parentRun.RunId),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowDeeperHandoff_WhenExecutionPolicyOverridesMaxDepth()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun() with
        {
            ParentRunId = "ancestor-3",
            RootRunId = "ancestor-root",
            DelegatedByRunId = "ancestor-3",
            DelegatedByAgent = "router"
        };
        var allRuns = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        allRuns[parentRun.RunId] = parentRun;
        allRuns["ancestor-3"] = new AgentRun { RunId = "ancestor-3", ParentRunId = "ancestor-2" };
        allRuns["ancestor-2"] = new AgentRun { RunId = "ancestor-2", ParentRunId = "ancestor-1" };
        allRuns["ancestor-1"] = new AgentRun { RunId = "ancestor-1", ParentRunId = string.Empty };
        AgentRun? latestParentRun = parentRun;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]",
            executionPolicy: "{\"maxHandoffDepth\":4}");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var runId = call.Arg<string>();
                if (runId == parentRun.RunId)
                {
                    return latestParentRun;
                }

                return allRuns.TryGetValue(runId, out var run) ? run : null;
            });
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-final"),
                new GenerateTextResult("parent-final"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please help", "delegate_and_wait"));
        stepDecisionParser.Parse("child-final")
            .Returns(StepDecision.Respond("Child answer"));
        stepDecisionParser.Parse("parent-final")
            .Returns(StepDecision.Respond("Parent answer"));

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    allRuns[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                allRuns[updated.RunId] = updated;
                if (updated.RunId == parentRun.RunId)
                {
                    latestParentRun = updated;
                }
                else
                {
                    childRun = updated;
                }
            });

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHandoff");
        Assert.Equal("WaitingHandoff", waitingRun.Status);
        Assert.NotNull(childRun);
        Assert.Equal(parentRun.RunId, childRun!.ParentRunId);

        var completedChildRun = await WaitForChildRunCompletionAsync(agentRunRepo, parentRun.RunId);
        Assert.Equal("Child answer", completedChildRun.OutputPayload);
        var completedParentRun = await WaitForFinalParentCompletionAsync(agentRunRepo, parentRun.RunId);
        Assert.Equal("Parent answer", completedParentRun.OutputPayload);
        await agentRunRepo.Received(1).AddAsync(
            Arg.Is<AgentRun>(run => run.RunId == childRun.RunId),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldEnterManualCompensation_WhenCompletedToolResultEnvelopeIsFailed()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var pendingInvocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            ToolName = "weather",
            Mode = "async",
            Status = "Pending",
            InputPayload = "{\"city\":\"Shanghai\"}",
            CallbackToken = run.CurrentWaitToken,
            StartedAt = DateTime.UtcNow,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };
        var resolvedDefinition = CreateResolvedDefinition();
        toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                ToolName = "weather",
                CompensationPolicy = "{\"mode\":\"manual\"}"
            });

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);

        var invocationRepository = CreateToolInvocationRepository(pendingInvocation);
        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            invocationRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(
            new ResumeAgentRunMessage(
                run.RunId,
                run.CurrentWaitToken,
                """{"status":"failed","error":{"code":"backend_down","message":"tool backend crashed"}}""",
                "invocation-1"),
            cts.Token);

        var failedPendingStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Failed");
        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHuman");
        var waitingHumanStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "human_wait", status: "Pending");
        var waitingHumanEvent = await WaitForEventAsync(eventBus, eventType: "waiting_human");
        var failedInvocation = await invocationRepository.GetByInvocationIdAsync("invocation-1", CancellationToken.None);

        Assert.Equal("WaitingHuman", waitingRun.Status);
        Assert.False(string.IsNullOrWhiteSpace(waitingRun.CurrentWaitToken));
        Assert.Equal("Failed", failedPendingStep.Status);
        Assert.True(ToolFailurePayloadSerializer.TryParse(failedPendingStep.ErrorPayload, out var pendingFailurePayload));
        Assert.NotNull(pendingFailurePayload);
        Assert.Equal("weather", pendingFailurePayload!.ToolName);
        Assert.Equal("execution", pendingFailurePayload.Stage);
        Assert.Contains("tool backend crashed", pendingFailurePayload.Message);
        Assert.NotNull(pendingFailurePayload.Compensation);
        Assert.Equal("manual", pendingFailurePayload.Compensation!.Mode);
        Assert.NotNull(failedInvocation);
        Assert.Equal("Failed", failedInvocation!.Status);
        Assert.True(ToolFailurePayloadSerializer.TryParse(failedInvocation.ErrorPayload, out var invocationFailurePayload));
        Assert.NotNull(invocationFailurePayload);
        Assert.Equal("weather", invocationFailurePayload!.ToolName);
        Assert.Equal("execution", invocationFailurePayload.Stage);
        Assert.Contains("backend_down", invocationFailurePayload.Message);
        var waitingPayload = HumanApprovalPayloadSerializer.Parse(waitingHumanStep.DecisionPayload);
        Assert.Equal("tool_failure", waitingPayload.SourceType);
        Assert.Equal(pendingStep.StepId, waitingPayload.SourceStepId);
        Assert.Equal("invocation-1", waitingPayload.SourceInvocationId);
        Assert.Equal("weather", waitingPayload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", waitingPayload.SourceToolInput);
        Assert.Equal("Failed", waitingPayload.SourceToolStatus);
        Assert.True(waitingPayload.ContinueAsToolResult);
        Assert.Contains("manual compensation", waitingPayload.Comment, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("waiting_human", waitingHumanEvent.EventType);
        Assert.Equal("human_wait", waitingHumanEvent.Data.StepType);
        await modelGateway.DidNotReceive().GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.DidNotReceive().RecordToolResultAsync(
            Arg.Any<AgentRun>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldApprovePendingStep_RunTool_UpdateApproval_AndPublishDoneEvent()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateApprovalWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", "internal_write"))
        };
        var approval = CreatePendingApproval(run.RunId, pendingStep.StepId);
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        agentApprovalRepo.GetByRunIdAndStepIdAsync(run.RunId, pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(approval);
        toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "sunny"));
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new ApproveAgentRunStepMessage(run.RunId, pendingStep.StepId, "u-1", "Alice", "admin", "Looks good"), cts.Token);

        var completedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        var completedPendingStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Completed");
        var updatedApproval = await WaitForUpdatedApprovalAsync(agentApprovalRepo, expectedDecision: ApprovalDecisions.Approved);
        await WaitForEventAsync(eventBus, eventType: "done");

        Assert.Equal("Completed", completedRun.Status);
        Assert.Equal("All done", completedRun.OutputPayload);
        Assert.Equal("Completed", completedPendingStep.Status);
        Assert.Equal("sunny", completedPendingStep.OutputPayload);
        var approvedPayload = ApprovalPayloadSerializer.Parse(completedPendingStep.DecisionPayload);
        Assert.Equal(ApprovalDecisions.Approved, approvedPayload.Decision);
        Assert.Equal("Looks good", approvedPayload.Comment);
        Assert.Equal("Alice", updatedApproval.ApproverName);
        Assert.Equal("admin", updatedApproval.ApproverRole);
        Assert.Equal("Looks good", updatedApproval.Comment);
        Assert.Contains(eventBus.Events, evt => evt.EventType == "step" && evt.Data.Output == "sunny");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "All done");
        await runtimeMemoryWriter.Received(1).RecordToolResultAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId),
            "weather",
            "{\"city\":\"Shanghai\"}",
            "sunny",
            true,
            true,
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId && value.Status == "Completed"),
            "All done",
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldOnlyPersistUserMemory_WhenMemoryPolicyDisallowsSessionToolMemory()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var pendingInvocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            ToolName = "weather",
            Mode = "async",
            Status = "Pending",
            InputPayload = "{\"city\":\"Shanghai\"}",
            CallbackToken = run.CurrentWaitToken,
            StartedAt = DateTime.UtcNow,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };
        var resolvedDefinition = CreateResolvedDefinition(
            memoryPolicy: """
            {"toolResultMemoryAllowedTools":["profile_tool"]}
            """);

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L, 3L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            CreateToolInvocationRepository(pendingInvocation));
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new ResumeAgentRunMessage(run.RunId, run.CurrentWaitToken, "sunny", "invocation-1"), cts.Token);

        await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        await WaitForEventAsync(eventBus, eventType: "done");

        await runtimeMemoryWriter.Received(1).RecordToolResultAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId),
            "weather",
            "{\"city\":\"Shanghai\"}",
            "sunny",
            false,
            true,
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId && value.Status == "Completed"),
            "All done",
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipSummaryMemoryWrite_WhenMemoryPolicyDisablesSummaryWrite()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var pendingInvocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            ToolName = "weather",
            Mode = "async",
            Status = "Pending",
            InputPayload = "{\"city\":\"Shanghai\"}",
            CallbackToken = run.CurrentWaitToken,
            StartedAt = DateTime.UtcNow,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };
        var resolvedDefinition = CreateResolvedDefinition(
            memoryPolicy: """
            {"summaryMemoryWriteEnabled":false}
            """);

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L, 3L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            CreateToolInvocationRepository(pendingInvocation));
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new ResumeAgentRunMessage(run.RunId, run.CurrentWaitToken, "sunny", "invocation-1"), cts.Token);

        await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        await WaitForEventAsync(eventBus, eventType: "done");

        await runtimeMemoryWriter.Received(1).RecordToolResultAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId),
            "weather",
            "{\"city\":\"Shanghai\"}",
            "sunny",
            true,
            true,
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.DidNotReceive().RecordRunCompletionSummaryAsync(
            Arg.Any<AgentRun>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCompleteHumanWait_AndPublishDoneEvent()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateHumanWaitingRun();
        var pendingStep = CreatePendingHumanStep(run, "Need operator review");

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(
            new CompleteHumanAgentRunMessage(
                run.RunId,
                pendingStep.StepId,
                run.CurrentWaitToken,
                "Human supplied answer",
                "Resolved manually",
                false,
                "u-2",
                "Bob",
                "operator"),
            cts.Token);

        var completedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        var completedStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Completed");
        await WaitForEventAsync(eventBus, eventType: "done");

        Assert.Equal("Completed", completedRun.Status);
        Assert.Equal("Human supplied answer", completedRun.OutputPayload);
        Assert.Equal(run.StatusVersion + 1, completedRun.StatusVersion);
        Assert.Equal("Completed", completedStep.Status);
        Assert.Equal("Human supplied answer", completedStep.OutputPayload);

        var payload = HumanApprovalPayloadSerializer.Parse(completedStep.DecisionPayload);
        Assert.Equal(ApprovalDecisions.Approved, payload.Decision);
        Assert.Equal("Resolved manually", payload.Comment);
        Assert.Equal("Bob", payload.HumanOperatorName);
        Assert.Equal("operator", payload.HumanOperatorRole);
        Assert.Equal("Human supplied answer", payload.HumanResult);

        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "step" &&
            evt.Data.StepType == "human_wait" &&
            evt.Data.Status == "Completed" &&
            evt.Data.Output == "Human supplied answer");
        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "done" &&
            evt.Data.Status == "Completed" &&
            evt.Data.Output == "Human supplied answer");
        await runOutboxRepo.Received(1).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.EventType == "step" &&
                evt.RunStatus == "Completed" &&
                evt.Payload != null &&
                evt.Payload.Contains("Human supplied answer")),
            Arg.Any<CancellationToken>());
        await runOutboxRepo.Received(1).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.EventType == "done" &&
                evt.RunStatus == "Completed" &&
                evt.Payload != null &&
                evt.Payload.Contains("Human supplied answer")),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTerminateHumanWait_AndPublishErrorEvent()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateHumanWaitingRun();
        var pendingStep = CreatePendingHumanStep(run, "Need operator review");

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(
            new CompleteHumanAgentRunMessage(
                run.RunId,
                pendingStep.StepId,
                run.CurrentWaitToken,
                null,
                "Stop the run",
                true,
                "u-2",
                "Bob",
                "operator"),
            cts.Token);

        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        var failedStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Failed");
        await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Failed", failedRun.Status);
        Assert.Equal("Stop the run", failedRun.InterruptReason);
        Assert.Equal("Failed", failedStep.Status);
        Assert.Equal("Stop the run", failedStep.ErrorPayload);

        var payload = HumanApprovalPayloadSerializer.Parse(failedStep.DecisionPayload);
        Assert.Equal(ApprovalDecisions.Rejected, payload.Decision);
        Assert.Equal("Stop the run", payload.Comment);
        Assert.Equal("Bob", payload.HumanOperatorName);

        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "step" &&
            evt.Data.StepType == "human_wait" &&
            evt.Data.Status == "Failed" &&
            evt.Data.Error == "Stop the run");
        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "error" &&
            evt.Data.StepType == "human_wait" &&
            evt.Data.Status == "Failed" &&
            evt.Data.Error == "Stop the run");
        await runOutboxRepo.Received(1).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.EventType == "error" &&
                evt.RunStatus == "Failed" &&
                evt.Payload != null &&
                evt.Payload.Contains("Stop the run")),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseHumanReplacementAsToolResult_AndContinueRun()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateHumanProcessingRun();
        var pendingStep = CreatePendingHumanStep(
            run,
            "Need operator review",
            sourceType: "tool_wait",
            sourceStepId: "tool-step-1",
            sourceInvocationId: "inv-1",
            sourceToolName: "weather",
            sourceToolInput: "{\"city\":\"Shanghai\"}",
            continueAsToolResult: true);
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L, 3L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(
            new CompleteHumanAgentRunMessage(
                run.RunId,
                pendingStep.StepId,
                "human-wait-1",
                "sunny",
                "Operator replaced the missing tool callback",
                false,
                "u-2",
                "Bob",
                "operator"),
            cts.Token);

        var completedHumanStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Completed");
        var completedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        var finalModelStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "model_call", status: "Completed");
        await WaitForEventAsync(eventBus, eventType: "done");

        Assert.Equal("Completed", completedRun.Status);
        Assert.Equal("All done", completedRun.OutputPayload);
        Assert.Equal("Completed", completedHumanStep.Status);
        Assert.Equal("sunny", completedHumanStep.OutputPayload);

        var payload = HumanApprovalPayloadSerializer.Parse(completedHumanStep.DecisionPayload);
        Assert.Equal("tool_wait", payload.SourceType);
        Assert.Equal("tool-step-1", payload.SourceStepId);
        Assert.Equal("inv-1", payload.SourceInvocationId);
        Assert.Equal("weather", payload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", payload.SourceToolInput);
        Assert.True(payload.ContinueAsToolResult);
        Assert.Equal("sunny", payload.HumanResult);

        Assert.Contains("Tool called:", finalModelStep.InputPayload);
        Assert.Contains("weather", finalModelStep.InputPayload);
        Assert.Contains("Tool result:", finalModelStep.InputPayload);
        Assert.Contains("sunny", finalModelStep.InputPayload);
        Assert.Contains("provided by a human operator", finalModelStep.InputPayload);
        await runtimeMemoryWriter.Received(1).RecordToolResultAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId),
            "weather",
            "{\"city\":\"Shanghai\"}",
            "sunny",
            true,
            true,
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId && value.Status == "Completed"),
            "All done",
            Arg.Any<CancellationToken>());
        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "step" &&
            evt.Data.StepType == "human_wait" &&
            evt.Data.Status == "Completed" &&
            evt.Data.Output == "sunny");
        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "done" &&
            evt.Data.Status == "Completed" &&
            evt.Data.Output == "All done");

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseHumanOverrideForCompletedToolResult_AndPreserveOriginalOutputInPrompt()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateHumanProcessingRun();
        var pendingStep = CreatePendingHumanStep(
            run,
            "Override completed tool result",
            sourceType: "tool_result",
            sourceStepId: "tool-step-1",
            sourceInvocationId: "inv-1",
            sourceToolName: "weather",
            sourceToolInput: "{\"city\":\"Shanghai\"}",
            sourceToolOutput: "rainy",
            sourceToolStatus: "Completed",
            continueAsToolResult: true);
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L, 3L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(
            new CompleteHumanAgentRunMessage(
                run.RunId,
                pendingStep.StepId,
                "human-wait-1",
                "sunny",
                "Operator corrected the completed tool result",
                false,
                "u-2",
                "Bob",
                "operator"),
            cts.Token);

        var completedHumanStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Completed");
        var completedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        var finalModelStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "model_call", status: "Completed");
        await WaitForEventAsync(eventBus, eventType: "done");

        Assert.Equal("Completed", completedRun.Status);
        Assert.Equal("All done", completedRun.OutputPayload);
        Assert.Equal("Completed", completedHumanStep.Status);
        Assert.Equal("sunny", completedHumanStep.OutputPayload);

        var payload = HumanApprovalPayloadSerializer.Parse(completedHumanStep.DecisionPayload);
        Assert.Equal("tool_result", payload.SourceType);
        Assert.Equal("tool-step-1", payload.SourceStepId);
        Assert.Equal("inv-1", payload.SourceInvocationId);
        Assert.Equal("weather", payload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", payload.SourceToolInput);
        Assert.Equal("rainy", payload.SourceToolOutput);
        Assert.Equal("Completed", payload.SourceToolStatus);
        Assert.True(payload.ContinueAsToolResult);

        Assert.Contains("Original tool result before human override:", finalModelStep.InputPayload);
        Assert.Contains("rainy", finalModelStep.InputPayload);
        Assert.Contains("Tool result:", finalModelStep.InputPayload);
        Assert.Contains("sunny", finalModelStep.InputPayload);
        Assert.Contains("Operator corrected the completed tool result", finalModelStep.InputPayload);
        await runtimeMemoryWriter.Received(1).RecordToolResultAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId),
            "weather",
            "{\"city\":\"Shanghai\"}",
            "sunny",
            true,
            true,
            Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.Received(1).RecordRunCompletionSummaryAsync(
            Arg.Is<AgentRun>(value => value.RunId == run.RunId && value.Status == "Completed"),
            "All done",
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailHumanReplacement_WhenReplacementViolatesOutputSchema()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateHumanProcessingRun();
        var pendingStep = CreatePendingHumanStep(
            run,
            "Override completed tool result",
            sourceType: "tool_result",
            sourceStepId: "tool-step-1",
            sourceInvocationId: "inv-1",
            sourceToolName: "weather",
            sourceToolInput: "{\"city\":\"Shanghai\"}",
            sourceToolOutput: "{\"forecast\":\"rainy\"}",
            sourceToolStatus: "Completed",
            continueAsToolResult: true);
        var resolvedDefinition = CreateResolvedDefinition();
        toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                ToolName = "weather",
                OutputSchema = "{\"type\":\"object\",\"required\":[\"forecast\"],\"properties\":{\"forecast\":{\"type\":\"string\"}},\"additionalProperties\":false}"
            });

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(
            new CompleteHumanAgentRunMessage(
                run.RunId,
                pendingStep.StepId,
                "human-wait-1",
                "sunny",
                "Operator corrected the completed tool result",
                false,
                "u-2",
                "Bob",
                "operator"),
            cts.Token);

        var completedHumanStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Completed");
        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Completed", completedHumanStep.Status);
        Assert.Equal("sunny", completedHumanStep.OutputPayload);
        Assert.Equal("Failed", failedRun.Status);
        Assert.Contains("Output for tool 'weather'", failedRun.InterruptReason);
        await modelGateway.DidNotReceive().GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());
        await runtimeMemoryWriter.DidNotReceive().RecordToolResultAsync(
            Arg.Any<AgentRun>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RuntimeMemoryWriter_ShouldPersistToolResultAndCompletionSummary()
    {
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var agentStepRepository = Substitute.For<IAgentStepRepository>();
        var logger = NullLogger<RuntimeMemoryWriter>.Instance;
        var writer = new RuntimeMemoryWriter(sessionMemoryRepository, userMemoryRepository, summaryMemoryRepository, agentStepRepository, logger);
        var run = CreateWaitingRun() with
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1"
        };
        agentStepRepository.ListByRunIdAsync(run.RunId, Arg.Any<CancellationToken>())
            .Returns(
            [
                AgentRunLoop.CreateStep(run.RunId, 1, "created", "Completed", null, null, null, DateTime.UtcNow, DateTime.UtcNow),
                AgentRunLoop.CreateStep(run.RunId, 2, "running", "Completed", "hello", null, null, DateTime.UtcNow, DateTime.UtcNow),
                AgentRunLoop.CreateStep(run.RunId, 3, "model_call", "Completed", "hello", "final-answer", null, DateTime.UtcNow, DateTime.UtcNow)
            ]);

        await writer.RecordToolResultAsync(
            run,
            "weather",
            "{\"city\":\"Shanghai\",\"apiKey\":\"secret-1\",\"nested\":{\"token\":\"token-1\"}}",
            "{\"forecast\":\"sunny\",\"secret\":\"secret-2\"}",
            true,
            true,
            CancellationToken.None);
        await writer.RecordRunCompletionSummaryAsync(run with { Status = "Completed" }, "All done", CancellationToken.None);

        await sessionMemoryRepository.Received(1).AddAsync(
            Arg.Is<SessionMemory>(memory =>
                memory.TenantId == "tenant-1" &&
                memory.UserId == "user-1" &&
                memory.SessionId == "session-1" &&
                memory.RunId == run.RunId &&
                memory.MemoryType == "tool_result" &&
                memory.SourceType == "tool_result" &&
                memory.SourceRef == "run-001:weather" &&
                HasMaskedToolResultMemory(memory.ContentJson)),
            Arg.Any<CancellationToken>());
        await userMemoryRepository.DidNotReceive().AddAsync(Arg.Any<UserMemory>(), Arg.Any<CancellationToken>());
        await summaryMemoryRepository.Received(1).AddAsync(
            Arg.Is<SummaryMemory>(memory =>
                memory.TenantId == "tenant-1" &&
                memory.RunId == run.RunId &&
                memory.SessionId == "session-1" &&
                memory.SummaryType == "run_completion" &&
                memory.GeneratedByModel == "runtime_template" &&
                memory.SourceStartSeq == 1 &&
                memory.SourceEndSeq == 3 &&
                memory.SummaryText.Contains("User request:") &&
                memory.SummaryText.Contains("hello") &&
                memory.SummaryText.Contains("Final outcome:") &&
                memory.SummaryText.Contains("All done")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RuntimeMemoryWriter_ShouldPersistStructuredUserMemoryFromTrustedToolOutput()
    {
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var agentStepRepository = Substitute.For<IAgentStepRepository>();
        var logger = NullLogger<RuntimeMemoryWriter>.Instance;
        var writer = new RuntimeMemoryWriter(sessionMemoryRepository, userMemoryRepository, summaryMemoryRepository, agentStepRepository, logger);
        var run = CreateWaitingRun() with
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1"
        };
        var toolOutput = """
            {
              "status": "ok",
              "userMemories": [
                {
                  "memoryKey": "seat_preference",
                  "memoryType": "preference",
                  "memoryValue": "\"window\"",
                  "confidence": 0.95,
                  "expiresAt": "2026-07-01T00:00:00Z"
                }
              ]
            }
            """;

        await writer.RecordToolResultAsync(run, "profile_tool", "{\"userId\":\"user-1\"}", toolOutput, true, true, CancellationToken.None);

        await userMemoryRepository.Received(1).GetByMemoryKeyAsync("tenant-1", "user-1", "seat_preference", Arg.Any<CancellationToken>());
        await userMemoryRepository.Received(1).AddAsync(
            Arg.Is<UserMemory>(memory =>
                memory.TenantId == "tenant-1" &&
                memory.UserId == "user-1" &&
                memory.MemoryKey == "seat_preference" &&
                memory.MemoryScope == "user" &&
                memory.MemoryType == "preference" &&
                memory.MemoryValue == "\"window\"" &&
                memory.SourceType == "tool_memory" &&
                memory.SourceRef == "run-001:profile_tool" &&
                memory.Confidence == 0.95m &&
                memory.ExpiresAt == new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RuntimeMemoryWriter_ShouldPersistOnlyUserMemory_WhenSessionWriteDisabled()
    {
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var agentStepRepository = Substitute.For<IAgentStepRepository>();
        var logger = NullLogger<RuntimeMemoryWriter>.Instance;
        var writer = new RuntimeMemoryWriter(sessionMemoryRepository, userMemoryRepository, summaryMemoryRepository, agentStepRepository, logger);
        var run = CreateWaitingRun() with
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1"
        };
        var toolOutput = """
            {
              "userMemories": [
                {
                  "memoryKey": "seat_preference",
                  "memoryType": "preference",
                  "memoryValue": "\"window\""
                }
              ]
            }
            """;

        await writer.RecordToolResultAsync(run, "profile_tool", "{\"userId\":\"user-1\"}", toolOutput, false, true, CancellationToken.None);

        await sessionMemoryRepository.DidNotReceive().AddAsync(Arg.Any<SessionMemory>(), Arg.Any<CancellationToken>());
        await userMemoryRepository.Received(1).AddAsync(
            Arg.Is<UserMemory>(memory =>
                memory.TenantId == "tenant-1" &&
                memory.UserId == "user-1" &&
                memory.MemoryKey == "seat_preference" &&
                memory.SourceRef == "run-001:profile_tool"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RuntimeMemoryWriter_ShouldSkipUserMemory_WhenUserWriteDisabled()
    {
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var agentStepRepository = Substitute.For<IAgentStepRepository>();
        var logger = NullLogger<RuntimeMemoryWriter>.Instance;
        var writer = new RuntimeMemoryWriter(sessionMemoryRepository, userMemoryRepository, summaryMemoryRepository, agentStepRepository, logger);
        var run = CreateWaitingRun() with
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1"
        };
        var toolOutput = """
            {
              "userMemories": [
                {
                  "memoryKey": "seat_preference",
                  "memoryType": "preference",
                  "memoryValue": "\"window\""
                }
              ]
            }
            """;

        await writer.RecordToolResultAsync(run, "profile_tool", "{\"userId\":\"user-1\"}", toolOutput, true, false, CancellationToken.None);

        await sessionMemoryRepository.Received(1).AddAsync(Arg.Any<SessionMemory>(), Arg.Any<CancellationToken>());
        await userMemoryRepository.DidNotReceive().GetByMemoryKeyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await userMemoryRepository.DidNotReceive().AddAsync(Arg.Any<UserMemory>(), Arg.Any<CancellationToken>());
        await userMemoryRepository.DidNotReceive().UpdateAsync(Arg.Any<UserMemory>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RuntimeMemoryWriter_ShouldUpdateExistingUserMemory_WhenMemoryKeyAlreadyExists()
    {
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var agentStepRepository = Substitute.For<IAgentStepRepository>();
        var logger = NullLogger<RuntimeMemoryWriter>.Instance;
        var writer = new RuntimeMemoryWriter(sessionMemoryRepository, userMemoryRepository, summaryMemoryRepository, agentStepRepository, logger);
        var run = CreateWaitingRun() with
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1"
        };
        userMemoryRepository.GetByMemoryKeyAsync("tenant-1", "user-1", "seat_preference", Arg.Any<CancellationToken>())
            .Returns(new UserMemory
            {
                Id = "memory-1",
                TenantId = "tenant-1",
                UserId = "user-1",
                MemoryKey = "seat_preference",
                MemoryScope = "user",
                MemoryType = "preference",
                MemoryValue = "\"aisle\"",
                SourceType = "tool_memory",
                SourceRef = "run-000:profile_tool",
                Confidence = 0.8m
            });
        var toolOutput = """
            {
              "userMemories": [
                {
                  "memoryKey": "seat_preference",
                  "memoryType": "preference",
                  "memoryValue": "\"window\"",
                  "confidence": 1.0
                }
              ]
            }
            """;

        await writer.RecordToolResultAsync(run, "profile_tool", null, toolOutput, true, true, CancellationToken.None);

        await userMemoryRepository.DidNotReceive().AddAsync(Arg.Any<UserMemory>(), Arg.Any<CancellationToken>());
        await userMemoryRepository.Received(1).UpdateAsync(
            Arg.Is<UserMemory>(memory =>
                memory.Id == "memory-1" &&
                memory.MemoryValue == "\"window\"" &&
                memory.Confidence == 1.0m &&
                memory.SourceRef == "run-001:profile_tool"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectPendingApproval_UpdateApproval_AndPublishErrorEvent()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateApprovalWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", "internal_write"))
        };
        var approval = CreatePendingApproval(run.RunId, pendingStep.StepId);

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentApprovalRepo.GetByRunIdAndStepIdAsync(run.RunId, pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(approval);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new RejectAgentRunStepMessage(run.RunId, pendingStep.StepId, "Denied", "u-1", "Alice", "admin"), cts.Token);

        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        var failedStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Failed");
        var updatedApproval = await WaitForUpdatedApprovalAsync(agentApprovalRepo, expectedDecision: ApprovalDecisions.Rejected);
        await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Denied", failedRun.InterruptReason);
        Assert.Equal("Failed", failedStep.Status);
        Assert.Equal("Denied", failedStep.ErrorPayload);
        var rejectedPayload = ApprovalPayloadSerializer.Parse(failedStep.DecisionPayload);
        Assert.Equal(ApprovalDecisions.Rejected, rejectedPayload.Decision);
        Assert.Equal("Denied", updatedApproval.Comment);
        Assert.Equal("Alice", updatedApproval.ApproverName);
        await toolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCreateApproval_WhenLoopWaitsForApproval()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun();
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));
        stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Description = "Weather tool",
                Enabled = true,
                SideEffectLevel = "internal_write"
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingApproval");
        var createdApproval = await WaitForAddedApprovalAsync(agentApprovalRepo);
        await WaitForEventAsync(eventBus, eventType: "waiting_approval");

        Assert.Equal("WaitingApproval", waitingRun.Status);
        Assert.Equal("Pending", createdApproval.Decision);
        Assert.Equal("weather", createdApproval.RequestedAction);
        Assert.Equal("internal_write", createdApproval.RiskLevel);
        Assert.Equal("run-001", createdApproval.RunId);
        Assert.NotNull(createdApproval.ExpiresAt);
        Assert.Equal(createdApproval.CreateTime.AddMinutes(15), createdApproval.ExpiresAt);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseBoundDefinitionApprovalPolicy_WhenWaitingForApproval()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun() with { AgentDefinitionVersionId = "ver-1" };
        var resolvedDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            approvalPolicy: "{\"approvalRequiredSideEffectLevels\":[],\"parameterApprovalRules\":[{\"toolName\":\"weather\",\"inputPath\":\"city\",\"expectedValue\":\"Shanghai\",\"overrideSideEffectLevel\":\"destructive\"}]}");

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetByVersionIdAsync("ver-1", Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));
        stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Description = "Weather tool",
                Enabled = true,
                SideEffectLevel = "read_only"
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingApproval");
        var createdApproval = await WaitForAddedApprovalAsync(agentApprovalRepo);
        await WaitForEventAsync(eventBus, eventType: "waiting_approval");

        Assert.Equal("WaitingApproval", waitingRun.Status);
        Assert.Equal("destructive", createdApproval.RiskLevel);
        await toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseStricterParentApprovalPolicy_ForChildRunWaitingApproval()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var parentRun = CreateRunningRun();
        var childRunStore = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);
        AgentRun? waitingParentRun = null;
        AgentRun? childRun = null;

        var parentDefinition = CreateResolvedDefinition(
            versionId: "ver-1",
            allowedHandoffs: "[\"support_agent\"]",
            approvalPolicy: "{\"approvalRequiredSideEffectLevels\":[\"internal_write\"],\"allowedApproverRoles\":[\"security\"],\"roleRequiredSideEffectLevels\":[\"internal_write\"]}");
        var childDefinition = CreateResolvedDefinition(
            versionId: "ver-2",
            agentCode: "support_agent",
            approvalPolicy: "{\"approvalRequiredSideEffectLevels\":[],\"allowedApproverRoles\":[\"security\",\"admin\"],\"roleRequiredSideEffectLevels\":[]}",
            allowedHandoffs: "[]");

        agentRunRepo.GetByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => waitingParentRun ?? parentRun);
        agentRunRepo.GetLatestChildByParentRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => childRun);
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(parentDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync("support_agent", Arg.Any<CancellationToken>())
            .Returns(childDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("handoff-decision"),
                new GenerateTextResult("child-tool-decision"));
        stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "Please help", "delegate_and_wait"));
        stepDecisionParser.Parse("child-tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Description = "Weather tool",
                Enabled = true,
                SideEffectLevel = "internal_write"
            });

        agentRunRepo.When(repo => repo.AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var added = call.Arg<AgentRun>();
                if (added.RunId != parentRun.RunId)
                {
                    childRun = added;
                    childRunStore[added.RunId] = added;
                }
            });
        agentRunRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var updated = call.Arg<AgentRun>();
                if (updated.RunId == parentRun.RunId)
                {
                    waitingParentRun = updated;
                }
                else
                {
                    childRun = updated;
                    childRunStore[updated.RunId] = updated;
                }
            });
        agentRunRepo.GetByRunIdAsync(Arg.Is<string>(id => childRunStore.ContainsKey(id)), Arg.Any<CancellationToken>())
            .Returns(call => childRunStore[call.Arg<string>()]);

        AgentStep? parentHandoffStep = null;
        agentStepRepo.GetLastByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ => parentHandoffStep);
        agentStepRepo.When(repo => repo.AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (step.RunId == parentRun.RunId && step.StepType == "handoff")
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.When(repo => repo.UpdateAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var step = call.Arg<AgentStep>();
                if (parentHandoffStep is not null && step.StepId == parentHandoffStep.StepId)
                {
                    parentHandoffStep = step;
                }
            });
        agentStepRepo.ListByRunIdAsync(parentRun.RunId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var steps = new List<AgentStep>();
                if (parentHandoffStep is not null)
                {
                    steps.Add(AgentRunLoop.CreateStep(parentRun.RunId, 3, "model_call", "Completed", "hello", "handoff-decision", null, DateTime.UtcNow, DateTime.UtcNow));
                    steps.Add(parentHandoffStep);
                }

                return (IReadOnlyList<AgentStep>)steps;
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(parentRun.RunId), cts.Token);

        var waitingChildRun = await WaitForChildRunStatusAsync(agentRunRepo, parentRun.RunId, "WaitingApproval");
        var createdApproval = await WaitForAddedApprovalAsync(agentApprovalRepo);

        Assert.Equal(waitingChildRun.RunId, createdApproval.RunId);
        Assert.Equal("internal_write", createdApproval.RiskLevel);
        await toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailRun_WhenFinalResponseViolatesDefinitionOutputSchema()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun();
        var resolvedDefinition = CreateResolvedDefinition(
            outputSchema: "{\"type\":\"object\",\"required\":[\"answer\"],\"properties\":{\"answer\":{\"type\":\"string\"}},\"additionalProperties\":false}");

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);

        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        var failedStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "failed", status: "Failed");
        var errorEvent = await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Failed", failedRun.Status);
        Assert.Contains("Output for agent 'writer'", failedRun.InterruptReason);
        Assert.Contains("Output for agent 'writer'", failedStep.ErrorPayload);
        Assert.Equal("respond", errorEvent.Data.StepType);
        Assert.Equal("Failed", errorEvent.Data.Status);
        Assert.Contains("Output for agent 'writer'", errorEvent.Data.Error);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseRunBoundDefinitionVersion_WhenCurrentDefinitionChanged()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateWaitingRun() with { AgentDefinitionVersionId = "ver-1" };
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = "{\"toolName\":\"weather\"}"
        };
        var pendingInvocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = pendingStep.StepId,
            ToolName = "weather",
            Mode = "async",
            Status = "Pending",
            InputPayload = "{\"city\":\"Shanghai\"}",
            CallbackToken = run.CurrentWaitToken,
            StartedAt = DateTime.UtcNow,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };
        var boundDefinition = CreateResolvedDefinition(versionId: "ver-1", systemPromptTemplate: "You are version one.");
        var currentDefinition = CreateResolvedDefinition(versionId: "ver-2", systemPromptTemplate: "You are version two.");

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetByVersionIdAsync("ver-1", Arg.Any<CancellationToken>()).Returns(boundDefinition);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(currentDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L, 3L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter,
            CreateToolInvocationRepository(pendingInvocation));
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(
            new ResumeAgentRunMessage(
                run.RunId,
                run.CurrentWaitToken,
                "\"sunny\"",
                "invocation-1"),
            cts.Token);

        await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Completed");
        await WaitForAddedStepAsync(agentStepRepo, stepType: "model_call", status: "Completed");

        await agentDefinitionRepo.Received(1).GetByVersionIdAsync("ver-1", Arg.Any<CancellationToken>());
        await agentDefinitionRepo.DidNotReceive().GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>());
        await modelGateway.Received(1).GenerateTextAsync(
            Arg.Is<GenerateTextRequest>(request =>
                request.SystemPrompt != null &&
                request.SystemPrompt.Contains("You are version one.") &&
                request.SystemPrompt.Contains("Use {\"action\":\"tool_call\",\"toolName\":\"...\",\"toolInput\":\"...\"} to call one tool.") &&
                request.SystemPrompt.Contains("Use {\"action\":\"handoff\",\"targetAgent\":\"...\",\"input\":\"...\",\"mode\":\"route_only\"} to route directly to another allowed agent.") &&
                request.SystemPrompt.Contains("Use {\"action\":\"handoff\",\"targetAgent\":\"...\",\"input\":\"...\",\"mode\":\"delegate_and_wait\"} to delegate to another allowed agent and then continue.") &&
                request.SystemPrompt.Contains("Use {\"action\":\"handoff\",\"targetAgent\":\"...\",\"input\":\"...\",\"mode\":\"delegate_and_merge\"} to delegate and then merge the child result into the final answer.") &&
                request.SystemPrompt.Contains("Optional handoff fields: \"reason\", \"confidence\", \"context_overrides\", \"memory_overrides\", \"tool_overrides\", \"approval_required\".")),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotAdvanceRun_WhenRunIsAlreadyCancelled()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun() with
        {
            Status = "Cancelled",
            InterruptReason = "User cancelled."
        };

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);
        await WaitForRunLookupAsync(agentRunRepo);

        await agentDefinitionRepo.DidNotReceive().GetEnabledByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await modelGateway.DidNotReceive().GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());
        await agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await agentStepRepo.DidNotReceive().AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        Assert.Empty(eventBus.Events);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepApprovalApproved_WhenApprovedToolBecomesPending()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateApprovalWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", "internal_write"))
        };
        var approval = CreatePendingApproval(run.RunId, pendingStep.StepId);
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        agentApprovalRepo.GetByRunIdAndStepIdAsync(run.RunId, pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(approval);
        toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Pending("weather", "tool-wait-1"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new ApproveAgentRunStepMessage(run.RunId, pendingStep.StepId, "u-1", "Alice", "admin", null), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingTool");
        var updatedApproval = await WaitForUpdatedApprovalAsync(agentApprovalRepo, expectedDecision: ApprovalDecisions.Approved);
        var pendingUpdatedStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Pending");

        Assert.Equal("WaitingTool", waitingRun.Status);
        Assert.Equal("Approved", updatedApproval.Decision);
        Assert.Equal("Pending", pendingUpdatedStep.Status);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailApprovedStep_WhenApprovedToolReturnsFailedResult()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateApprovalWaitingRun();
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", "internal_write"))
        };
        var approval = CreatePendingApproval(run.RunId, pendingStep.StepId);
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        agentApprovalRepo.GetByRunIdAndStepIdAsync(run.RunId, pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(approval);
        toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Failed("weather", "tool backend crashed"));
        toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                ToolName = "weather",
                CompensationPolicy = "{\"mode\":\"manual\"}"
            });

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new ApproveAgentRunStepMessage(run.RunId, pendingStep.StepId, "u-1", "Alice", "admin", "Looks good"), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHuman");
        var failedStep = await WaitForUpdatedStepAsync(agentStepRepo, expectedStatus: "Failed");
        var updatedApproval = await WaitForUpdatedApprovalAsync(agentApprovalRepo, expectedDecision: ApprovalDecisions.Approved);
        var waitingHumanStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "human_wait", status: "Pending");
        var waitingHumanEvent = await WaitForEventAsync(eventBus, eventType: "waiting_human");

        Assert.Equal("WaitingHuman", waitingRun.Status);
        Assert.Equal("Approved", updatedApproval.Decision);
        Assert.Equal("Failed", failedStep.Status);
        Assert.True(ToolFailurePayloadSerializer.TryParse(failedStep.ErrorPayload, out var approvedFailurePayload));
        Assert.NotNull(approvedFailurePayload);
        Assert.Equal("manual", approvedFailurePayload!.Compensation!.Mode);
        var waitingPayload = HumanApprovalPayloadSerializer.Parse(waitingHumanStep.DecisionPayload);
        Assert.Equal("tool_failure", waitingPayload.SourceType);
        Assert.Equal(pendingStep.StepId, waitingPayload.SourceStepId);
        Assert.Equal("weather", waitingPayload.SourceToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", waitingPayload.SourceToolInput);
        Assert.Equal("Failed", waitingPayload.SourceToolStatus);
        Assert.True(waitingPayload.ContinueAsToolResult);
        Assert.Contains("manual compensation", waitingPayload.Comment, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("waiting_human", waitingHumanEvent.EventType);
        Assert.Equal("human_wait", waitingHumanEvent.Data.StepType);
        Assert.Equal("Pending", waitingHumanEvent.Data.Status);
        await modelGateway.DidNotReceive().GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailRunAndPublishError_WhenModelExecutionThrows()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun();
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GenerateTextResult>>(_ => throw new InvalidOperationException("boom"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);

        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        var failedStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "failed", status: "Failed");
        var errorEvent = await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Failed", failedRun.Status);
        Assert.Equal("boom", failedRun.InterruptReason);

        Assert.Equal(run.CurrentStepNo + 1, failedStep.StepNo);
        Assert.Equal("boom", failedStep.ErrorPayload);
        Assert.Equal("hello", failedStep.InputPayload);

        Assert.Equal("failed", errorEvent.Data.StepType);
        Assert.Equal("Failed", errorEvent.Data.Status);
        Assert.Equal("boom", errorEvent.Data.Error);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldEnterWaitingHuman_WhenModelRequestsHuman()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun();
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("human-decision"));
        stepDecisionParser.Parse("human-decision")
            .Returns(StepDecision.RequestHuman("Need manual confirmation."));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);

        var waitingRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "WaitingHuman");
        var humanStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "human_wait", status: "Pending");
        var waitingEvent = await WaitForEventAsync(eventBus, eventType: "waiting_human");

        Assert.Equal("WaitingHuman", waitingRun.Status);
        Assert.False(string.IsNullOrWhiteSpace(waitingRun.CurrentWaitToken));
        Assert.Equal("Need manual confirmation.", humanStep.InputPayload);
        Assert.True(HumanApprovalPayloadSerializer.TryParse(humanStep.DecisionPayload, out var humanPayload));
        Assert.NotNull(humanPayload);
        Assert.Equal("run", humanPayload!.SourceType);
        Assert.False(humanPayload.ContinueAsToolResult);
        Assert.Equal("Need manual confirmation.", humanPayload.Comment);
        Assert.Equal("waiting_human", waitingEvent.EventType);
        Assert.Equal("human_wait", waitingEvent.Data.StepType);
        Assert.Equal("Pending", waitingEvent.Data.Status);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailRun_WhenModelReturnsFailDecision()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var runtimeMemoryWriter = Substitute.For<IRuntimeMemoryWriter>();
        var runOutboxRepo = Substitute.For<IRunOutboxEventRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun();
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        runOutboxRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(1L, 2L, 3L);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("fail-decision"));
        stepDecisionParser.Parse("fail-decision")
            .Returns(StepDecision.Fail("upstream_unavailable", "The upstream system is unavailable."));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            runOutboxRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository,
            runtimeMemoryWriter);
        var worker = new AgentRunWorker(
            channel,
            eventBus,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            new ApprovalTimeoutOptions { TimeoutMinutes = 15 });
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);

        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        var failedStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "failed", status: "Failed");
        var errorEvent = await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Failed", failedRun.Status);
        Assert.Equal("The upstream system is unavailable.", failedRun.InterruptReason);
        Assert.True(ModelFailurePayloadSerializer.TryParse(failedStep.ErrorPayload, out var failurePayload));
        Assert.NotNull(failurePayload);
        Assert.Equal("upstream_unavailable", failurePayload!.ErrorCode);
        Assert.Equal("The upstream system is unavailable.", failurePayload.Message);
        Assert.Equal("error", errorEvent.EventType);
        Assert.Equal("failed", errorEvent.Data.StepType);
        Assert.Equal("Failed", errorEvent.Data.Status);
        Assert.Contains("\"ErrorCode\":\"upstream_unavailable\"", errorEvent.Data.Error);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailRunWithToolCallStep_WhenToolExecutionThrows()
    {
        var channel = new AgentRunChannel();
        var eventBus = new RecordingEventBus();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();
        var agentApprovalRepo = Substitute.For<IAgentApprovalRepository>();
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var run = CreateRunningRun();
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));
        stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ToolExecutionResult>>(_ => throw new InvalidOperationException("Input for tool 'weather' is missing required property 'city'.")); 

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepo,
            null,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway,
            toolDefinitionRepository);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new CreateAgentRunMessage(run.RunId), cts.Token);

        var failedRun = await WaitForUpdatedRunAsync(agentRunRepo, expectedStatus: "Failed");
        var failedToolStep = await WaitForAddedStepAsync(agentStepRepo, stepType: "tool_call", status: "Failed");
        var errorEvent = await WaitForEventAsync(eventBus, eventType: "error");

        Assert.Equal("Failed", failedRun.Status);
        Assert.Equal("Input for tool 'weather' is missing required property 'city'.", failedRun.InterruptReason);
        Assert.Equal("tool_call", failedToolStep.StepType);
        Assert.Contains("\"toolName\":\"weather\"", failedToolStep.ErrorPayload);
        Assert.Contains("\"stage\":\"execution\"", failedToolStep.ErrorPayload);
        Assert.Contains("\"toolName\":\"weather\"", errorEvent.Data.Error);
        Assert.Contains(eventBus.Events, evt =>
            evt.EventType == "step" &&
            evt.Data.StepType == "tool_call" &&
            evt.Data.Status == "Failed" &&
            evt.Data.Error is not null &&
            evt.Data.Error.Contains("\"toolName\":\"weather\""));

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task<AgentRun> WaitForUpdatedRunAsync(IAgentRunRepository repository, string expectedStatus)
    {
        for (var i = 0; i < 100; i++)
        {
            var calls = repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentRunRepository.UpdateAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentRun>()
                .ToArray();
            var match = calls.LastOrDefault(run => run.Status == expectedStatus);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for AgentRun status '{expectedStatus}'.");
    }

    private static async Task<AgentStep> WaitForUpdatedStepAsync(IAgentStepRepository repository, string expectedStatus)
    {
        for (var i = 0; i < 100; i++)
        {
            var calls = repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.UpdateAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentStep>()
                .ToArray();
            var match = calls.LastOrDefault(step => step.Status == expectedStatus);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for AgentStep status '{expectedStatus}'.");
    }

    private static async Task<AgentApproval> WaitForAddedApprovalAsync(IAgentApprovalRepository repository)
    {
        for (var i = 0; i < 100; i++)
        {
            var calls = repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentApprovalRepository.AddAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentApproval>()
                .ToArray();
            var match = calls.LastOrDefault();
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for AgentApproval add.");
    }

    private static async Task<AgentApproval> WaitForUpdatedApprovalAsync(IAgentApprovalRepository repository, string expectedDecision)
    {
        for (var i = 0; i < 100; i++)
        {
            var calls = repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentApprovalRepository.UpdateAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentApproval>()
                .ToArray();
            var match = calls.LastOrDefault(approval => approval.Decision == expectedDecision);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for AgentApproval decision '{expectedDecision}'.");
    }

    private static async Task<AgentStep> WaitForAddedStepAsync(IAgentStepRepository repository, string stepType, string status)
    {
        for (var i = 0; i < 100; i++)
        {
            var calls = repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentStep>()
                .ToArray();
            var match = calls.LastOrDefault(step => step.StepType == stepType && step.Status == status);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for AgentStep '{stepType}' with status '{status}'.");
    }

    private static async Task<AgentRunEvent> WaitForEventAsync(RecordingEventBus eventBus, string eventType)
    {
        for (var i = 0; i < 100; i++)
        {
            lock (eventBus.Events)
            {
                var match = eventBus.Events.LastOrDefault(evt => evt.EventType == eventType);
                if (match is not null)
                {
                    return match;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for event '{eventType}'.");
    }

    private static async Task WaitForRunLookupAsync(IAgentRunRepository repository)
    {
        for (var i = 0; i < 100; i++)
        {
            var found = repository.ReceivedCalls()
                .Any(call => call.GetMethodInfo().Name == nameof(IAgentRunRepository.GetByRunIdAsync));
            if (found)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for AgentRun lookup.");
    }

    private static async Task<AgentRun> WaitForChildRunCompletionAsync(IAgentRunRepository repository, string parentRunId)
    {
        for (var i = 0; i < 100; i++)
        {
            var calls = repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentRunRepository.UpdateAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentRun>()
                .ToArray();
            var match = calls.LastOrDefault(run =>
                run.ParentRunId == parentRunId &&
                run.Status == "Completed");
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for child run of '{parentRunId}' to complete.");
    }

    private static async Task<AgentRun> WaitForChildRunStatusAsync(
        IAgentRunRepository repository,
        string parentRunId,
        string expectedStatus)
    {
        for (var i = 0; i < 100; i++)
        {
            var calls = repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentRunRepository.UpdateAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentRun>()
                .ToArray();
            var match = calls.LastOrDefault(run =>
                run.ParentRunId == parentRunId &&
                string.Equals(run.Status, expectedStatus, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for child run of '{parentRunId}' to reach status '{expectedStatus}'.");
    }

    private static async Task<AgentRun> WaitForFinalParentCompletionAsync(IAgentRunRepository repository, string parentRunId)
    {
        return await WaitForParentCompletionWithOutputAsync(repository, parentRunId, "Parent answer");
    }

    private static async Task<AgentRun> WaitForParentCompletionWithOutputAsync(
        IAgentRunRepository repository,
        string parentRunId,
        string expectedOutput)
    {
        for (var i = 0; i < 100; i++)
        {
            var calls = repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IAgentRunRepository.UpdateAsync))
                .Select(call => call.GetArguments()[0])
                .OfType<AgentRun>()
                .ToArray();
            var match = calls.LastOrDefault(run =>
                run.RunId == parentRunId &&
                run.Status == "Completed" &&
                run.OutputPayload == expectedOutput);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for parent run '{parentRunId}' final completion with output '{expectedOutput}'.");
    }

    private static bool HasMaskedToolResultMemory(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return false;
        }

        using var document = JsonDocument.Parse(contentJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("toolName", out var toolName)
            || toolName.GetString() != "weather"
            || !root.TryGetProperty("toolInput", out var toolInputElement)
            || !root.TryGetProperty("toolOutput", out var toolOutputElement))
        {
            return false;
        }

        var toolInput = toolInputElement.GetString();
        var toolOutput = toolOutputElement.GetString();
        return toolInput == "{\"city\":\"Shanghai\",\"apiKey\":\"***\",\"nested\":{\"token\":\"***\"}}"
            && toolOutput == "{\"forecast\":\"sunny\",\"secret\":\"***\"}"
            && !contentJson.Contains("secret-1", StringComparison.Ordinal)
            && !contentJson.Contains("token-1", StringComparison.Ordinal)
            && !contentJson.Contains("secret-2", StringComparison.Ordinal);
    }

    private static ServiceProvider CreateServiceProvider(
        IAgentRunRepository agentRunRepo,
        IAgentStepRepository agentStepRepo,
        IAgentApprovalRepository agentApprovalRepo,
        IRunOutboxEventRepository? runOutboxRepo,
        IAgentDefinitionRepository agentDefinitionRepo,
        IStepDecisionParser stepDecisionParser,
        IToolExecutor toolExecutor,
        IModelGateway modelGateway,
        IToolDefinitionRepository toolDefinitionRepository,
        IRuntimeMemoryWriter? runtimeMemoryWriter = null,
        IToolInvocationRepository? toolInvocationRepository = null,
        IToolOutputValidator? toolOutputValidator = null,
        IAgentOutputValidator? agentOutputValidator = null,
        IRuntimeContextComposer? runtimeContextComposer = null,
        IRouteRuleRepository? routeRuleRepository = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(agentRunRepo);
        services.AddSingleton(agentStepRepo);
        services.AddSingleton(agentApprovalRepo);
        if (runOutboxRepo is not null)
        {
            services.AddSingleton(runOutboxRepo);
        }

        services.AddSingleton(agentDefinitionRepo);
        services.AddSingleton(stepDecisionParser);
        services.AddSingleton(toolExecutor);
        services.AddSingleton(modelGateway);
        services.AddSingleton(toolDefinitionRepository);
        services.AddSingleton(toolOutputValidator ?? new JsonSchemaToolOutputValidator());
        services.AddSingleton(agentOutputValidator ?? new JsonSchemaAgentOutputValidator());
        if (runtimeMemoryWriter is not null)
        {
            services.AddSingleton(runtimeMemoryWriter);
        }

        if (runtimeContextComposer is not null)
        {
            services.AddSingleton(runtimeContextComposer);
        }

        if (routeRuleRepository is not null)
        {
            services.AddSingleton(routeRuleRepository);
        }

        services.AddSingleton<IToolInvocationRepository>(toolInvocationRepository ?? new InMemoryToolInvocationRepository());
        return services.BuildServiceProvider();
    }

    private static IToolInvocationRepository CreateToolInvocationRepository(params ToolInvocation[] invocations)
    {
        var repository = new InMemoryToolInvocationRepository();
        foreach (var invocation in invocations)
        {
            repository.AddAsync(invocation, CancellationToken.None).GetAwaiter().GetResult();
        }

        return repository;
    }

    private static AgentApproval CreatePendingApproval(string runId, string stepId)
    {
        var now = DateTime.UtcNow;
        return new AgentApproval
        {
            ApprovalId = "approval-1",
            RunId = runId,
            StepId = stepId,
            RequestedAction = "weather",
            RiskLevel = "internal_write",
            RequestPayload = "{\"city\":\"Shanghai\"}",
            Decision = ApprovalDecisions.Pending,
            WaitToken = "approval-123",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }

    private static AgentRun CreateWaitingRun()
    {
        return new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1",
            Status = "WaitingTool",
            InputPayload = "hello",
            CurrentStepNo = 4,
            CurrentWaitToken = "wait-123",
            StatusVersion = 2,
            MaxTurns = 5
        };
    }

    private static AgentRun CreateApprovalWaitingRun()
    {
        return new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1",
            Status = "WaitingApproval",
            InputPayload = "hello",
            CurrentStepNo = 4,
            CurrentWaitToken = "approval-123",
            StatusVersion = 2,
            MaxTurns = 5
        };
    }

    private static AgentRun CreateHumanWaitingRun()
    {
        return new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1",
            Status = "Running",
            InputPayload = "hello",
            CurrentStepNo = 5,
            CurrentWaitToken = "human-wait-1",
            StatusVersion = 3,
            MaxTurns = 5
        };
    }

    private static AgentRun CreateHumanProcessingRun()
    {
        return new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1",
            Status = "Running",
            InputPayload = "hello",
            CurrentStepNo = 5,
            CurrentWaitToken = string.Empty,
            StatusVersion = 4,
            MaxTurns = 5
        };
    }

    private static AgentRun CreateRunningRun()
    {
        return new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1",
            Status = "Running",
            InputPayload = "hello",
            CurrentStepNo = 2,
            StatusVersion = 1,
            MaxTurns = 5
        };
    }

    private static ResolvedAgentDefinition CreateResolvedDefinition(
        string? memoryPolicy = null,
        string versionId = "ver-1",
        string agentCode = "writer",
        string systemPromptTemplate = "You are a writer.",
        string? routingPolicy = null,
        string? approvalPolicy = null,
        string? executionPolicy = null,
        string? outputSchema = null,
        string? allowedTools = "[\"weather\"]",
        string? knowledgeSources = null,
        string? allowedHandoffs = "[]")
    {
        var now = DateTime.UtcNow;
        return new ResolvedAgentDefinition(
            new AgentDefinition
            {
                Id = "def-1",
                Code = agentCode,
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
                Id = versionId,
                AgentDefinitionId = "def-1",
                Version = 1,
                Status = "Published",
                Name = "Writer v1",
                DefaultModel = "gpt-4o",
                SystemPromptTemplate = systemPromptTemplate,
                AllowedTools = allowedTools,
                KnowledgeSources = knowledgeSources,
                AllowedHandoffs = allowedHandoffs,
                MemoryPolicy = memoryPolicy,
                RoutingPolicy = routingPolicy,
                ApprovalPolicy = approvalPolicy,
                ExecutionPolicy = executionPolicy,
                OutputSchema = outputSchema,
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

    private static AgentStep CreatePendingHumanStep(
        AgentRun run,
        string? comment,
        string? sourceType = null,
        string? sourceStepId = null,
        string? sourceInvocationId = null,
        string? sourceToolName = null,
        string? sourceToolInput = null,
        string? sourceToolOutput = null,
        string? sourceToolStatus = null,
        bool continueAsToolResult = false)
    {
        return AgentRunLoop.CreateStep(
            run.RunId,
            run.CurrentStepNo,
            "human_wait",
            "Pending",
            comment,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(
                HumanApprovalPayloadSerializer.CreatePending(
                    comment,
                    sourceType,
                    sourceStepId,
                    sourceInvocationId,
                    sourceToolName,
                    sourceToolInput,
                    sourceToolOutput,
                    sourceToolStatus,
                    continueAsToolResult))
        };
    }

    private sealed class InMemoryToolInvocationRepository : IToolInvocationRepository
    {
        private readonly List<ToolInvocation> _invocations = [];
        private readonly object _lock = new();

        public Task AddAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _invocations.Add(invocation);
            }

            return Task.CompletedTask;
        }

        public Task<ToolInvocation?> GetByInvocationIdAsync(string invocationId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult(_invocations.FirstOrDefault(x => x.InvocationId == invocationId && !x.Deleted));
            }
        }

        public Task<ToolInvocation?> GetPendingByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult(_invocations.FirstOrDefault(x =>
                    x.RunId == runId &&
                    x.StepId == stepId &&
                    x.Status == "Pending" &&
                    !x.Deleted));
            }
        }

        public Task<IReadOnlyList<ToolInvocation>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult((IReadOnlyList<ToolInvocation>)_invocations
                    .Where(x => x.RunId == runId && !x.Deleted)
                    .OrderBy(x => x.CreateTime)
                    .ToList());
            }
        }

        public Task UpdateAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                var index = _invocations.FindIndex(x => x.InvocationId == invocation.InvocationId);
                if (index >= 0)
                {
                    _invocations[index] = invocation;
                }
                else
                {
                    _invocations.Add(invocation);
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEventBus : IAgentRunEventBus
    {
        public List<AgentRunEvent> Events { get; } = [];

        public void Publish(AgentRunEvent evt)
        {
            lock (Events)
            {
                Events.Add(evt);
            }
        }

        public IAgentRunEventSubscription Subscribe(string runId)
        {
            return new EmptySubscription();
        }

        public async IAsyncEnumerable<AgentRunEvent> SubscribeAsync(string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await using var subscription = Subscribe(runId);
            await foreach (var evt in subscription.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }

        private sealed class EmptySubscription : IAgentRunEventSubscription
        {
            public async IAsyncEnumerable<AgentRunEvent> ReadAllAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.CompletedTask;
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
