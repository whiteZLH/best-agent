using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Runtime;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Integration;

public class AgentRunWaitingResumeIntegrationTests
{
    [Fact]
    public async Task ShouldCreateRun_WaitOnPendingTool_Resume_AndExposeFinalRunAndSteps()
    {
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var agentRunRepo = new InMemoryAgentRunRepository();
        var agentStepRepo = new InMemoryAgentStepRepository();
        var eventBus = new RecordingEventBus();
        var channel = new AgentRunChannel();
        var resolvedDefinition = CreateResolvedDefinition();

        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(resolvedDefinition);

        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("{\"action\":\"tool_call\",\"toolName\":\"weather\",\"toolInput\":\"{\\\"city\\\":\\\"Shanghai\\\"}\"}"),
                new GenerateTextResult("{\"action\":\"respond\",\"response\":\"The weather is sunny.\"}"));

        toolExecutor.ExecuteAsync(
                "weather",
                "{\"city\":\"Shanghai\"}",
                Arg.Any<ToolExecutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Pending("weather", "wait-123"));

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IAgentDefinitionRepository>(agentDefinitionRepo);
        services.AddSingleton<IAgentRunRepository>(agentRunRepo);
        services.AddSingleton<IAgentStepRepository>(agentStepRepo);
        services.AddSingleton<IModelGateway>(modelGateway);
        services.AddSingleton<IToolExecutor>(toolExecutor);
        services.AddSingleton<IAgentRunChannel>(channel);
        services.AddSingleton<IAgentRunEventBus>(eventBus);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<AgentRunWorker>>(NullLogger<AgentRunWorker>.Instance);
        services.AddSingleton<AgentRunWorker>();

        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var worker = serviceProvider.GetRequiredService<AgentRunWorker>();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);

        var createResult = await mediator.Send(new CreateAgentRunCommand("writer", "hello"), cts.Token);

        var waitingRun = await WaitForRunStatusAsync(agentRunRepo, createResult.RunId, "WaitingTool");
        Assert.Equal("wait-123", waitingRun.CurrentWaitToken);
        Assert.Equal(4, waitingRun.CurrentStepNo);

        var waitingSnapshot = await mediator.Send(new GetAgentRunByIdQuery(createResult.RunId), cts.Token);
        Assert.NotNull(waitingSnapshot);
        Assert.Equal("WaitingTool", waitingSnapshot!.Status);

        var waitingSteps = await mediator.Send(new GetAgentRunStepsQuery(createResult.RunId), cts.Token);
        Assert.Contains(waitingSteps, step => step.StepType == "tool_call" && step.Status == "Pending");

        var resumeResult = await mediator.Send(new ResumeAgentRunCommand(createResult.RunId, "wait-123", "sunny"), cts.Token);
        Assert.Equal("Running", resumeResult.Status);

        var completedRun = await WaitForRunStatusAsync(agentRunRepo, createResult.RunId, "Completed");
        Assert.Equal("The weather is sunny.", completedRun.OutputPayload);

        var completedSnapshot = await mediator.Send(new GetAgentRunByIdQuery(createResult.RunId), cts.Token);
        Assert.NotNull(completedSnapshot);
        Assert.Equal("Completed", completedSnapshot!.Status);
        Assert.Equal("The weather is sunny.", completedSnapshot.Output);

        var completedSteps = await mediator.Send(new GetAgentRunStepsQuery(createResult.RunId), cts.Token);
        Assert.Contains(completedSteps, step => step.StepType == "tool_call" && step.Status == "Completed" && step.Output == "sunny");
        Assert.Contains(completedSteps, step => step.StepType == "model_call" && step.Status == "Completed" && step.Output == "{\"action\":\"respond\",\"response\":\"The weather is sunny.\"}");

        Assert.Contains(eventBus.Events, evt => evt.EventType == "waiting" && evt.Data.Status == "Pending");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "The weather is sunny.");

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task<AgentRun> WaitForRunStatusAsync(InMemoryAgentRunRepository repository, string runId, string expectedStatus)
    {
        for (var i = 0; i < 100; i++)
        {
            var run = await repository.GetByRunIdAsync(runId, CancellationToken.None);
            if (run is not null && run.Status == expectedStatus)
            {
                return run;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for run '{runId}' status '{expectedStatus}'.");
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

    private sealed class InMemoryAgentRunRepository : IAgentRunRepository
    {
        private readonly Dictionary<string, AgentRun> _runs = new(StringComparer.Ordinal);
        private readonly object _lock = new();

        public Task AddAsync(AgentRun agentRun, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _runs[agentRun.RunId] = agentRun;
            }

            return Task.CompletedTask;
        }

        public Task<AgentRun?> GetByRunIdAsync(string runId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _runs.TryGetValue(runId, out var run);
                return Task.FromResult(run);
            }
        }

        public Task UpdateAsync(AgentRun agentRun, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _runs[agentRun.RunId] = agentRun;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAgentStepRepository : IAgentStepRepository
    {
        private readonly List<AgentStep> _steps = [];
        private readonly object _lock = new();

        public Task AddAsync(AgentStep agentStep, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _steps.Add(agentStep);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentStep>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult((IReadOnlyList<AgentStep>)_steps
                    .Where(step => step.RunId == runId)
                    .OrderBy(step => step.StepNo)
                    .ToList());
            }
        }

        public Task<AgentStep?> GetLastByRunIdAsync(string runId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult(_steps
                    .Where(step => step.RunId == runId)
                    .OrderByDescending(step => step.StepNo)
                    .FirstOrDefault());
            }
        }

        public Task UpdateAsync(AgentStep agentStep, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                var index = _steps.FindIndex(step => step.StepId == agentStep.StepId);
                if (index >= 0)
                {
                    _steps[index] = agentStep;
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

        public async IAsyncEnumerable<AgentRunEvent> SubscribeAsync(string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
