using System.Collections.Concurrent;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Runtime;
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
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
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
            DateTime.UtcNow);
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("All done"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway);
        var worker = new AgentRunWorker(channel, eventBus, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await channel.EnqueueAsync(new ResumeAgentRunMessage(run.RunId, run.CurrentWaitToken, "sunny"), cts.Token);

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
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var stepDecisionParser = Substitute.For<IStepDecisionParser>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var modelGateway = Substitute.For<IModelGateway>();
        var run = CreateRunningRun();
        var resolvedDefinition = CreateResolvedDefinition();

        agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        agentDefinitionRepo.GetEnabledByCodeAsync(run.AgentCode, Arg.Any<CancellationToken>()).Returns(resolvedDefinition);
        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GenerateTextResult>>(_ => throw new InvalidOperationException("boom"));

        using var services = CreateServiceProvider(
            agentRunRepo,
            agentStepRepo,
            agentDefinitionRepo,
            stepDecisionParser,
            toolExecutor,
            modelGateway);
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

    private static ServiceProvider CreateServiceProvider(
        IAgentRunRepository agentRunRepo,
        IAgentStepRepository agentStepRepo,
        IAgentDefinitionRepository agentDefinitionRepo,
        IStepDecisionParser stepDecisionParser,
        IToolExecutor toolExecutor,
        IModelGateway modelGateway)
    {
        var services = new ServiceCollection();
        services.AddSingleton(agentRunRepo);
        services.AddSingleton(agentStepRepo);
        services.AddSingleton(agentDefinitionRepo);
        services.AddSingleton(stepDecisionParser);
        services.AddSingleton(toolExecutor);
        services.AddSingleton(modelGateway);
        return services.BuildServiceProvider();
    }

    private static AgentRun CreateWaitingRun()
    {
        return new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            Status = "WaitingTool",
            InputPayload = "hello",
            CurrentStepNo = 4,
            CurrentWaitToken = "wait-123",
            StatusVersion = 2,
            MaxTurns = 5
        };
    }

    private static AgentRun CreateRunningRun()
    {
        return new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            Status = "Running",
            InputPayload = "hello",
            CurrentStepNo = 2,
            StatusVersion = 1,
            MaxTurns = 5
        };
    }

    private static ResolvedAgentDefinition CreateResolvedDefinition()
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

        public async IAsyncEnumerable<AgentRunEvent> SubscribeAsync(string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
