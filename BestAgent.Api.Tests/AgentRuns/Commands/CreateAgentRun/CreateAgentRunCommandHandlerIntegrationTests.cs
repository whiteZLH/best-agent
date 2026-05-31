using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using BestAgent.Infrastructure.Runtime;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.CreateAgentRun;

[Trait("Category", "Integration")]
public class CreateAgentRunCommandHandlerIntegrationTests
{
    [Fact]
    public async Task Handle_ShouldCreateRun_ExecuteWorker_AndCompleteRun()
    {
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var agentRunRepo = new InMemoryAgentRunRepository();
        var agentStepRepo = new InMemoryAgentStepRepository();
        var agentApprovalRepo = new InMemoryAgentApprovalRepository();
        var modelGateway = new FakeModelGateway(
            new GenerateTextResult("{\"action\":\"respond\",\"response\":\"Hi from fake model.\"}"));
        var toolExecutor = Substitute.For<IToolExecutor>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var resolvedDefinition = CreateResolvedDefinition();

        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(resolvedDefinition);

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IAgentDefinitionRepository>(agentDefinitionRepo);
        services.AddSingleton<IAgentRunRepository>(agentRunRepo);
        services.AddSingleton<IAgentStepRepository>(agentStepRepo);
        services.AddSingleton<IAgentApprovalRepository>(agentApprovalRepo);
        services.AddSingleton<IModelGateway>(modelGateway);
        services.AddSingleton<IToolExecutor>(toolExecutor);
        services.AddSingleton<IToolDefinitionRepository>(toolDefinitionRepository);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<AgentRunWorker>>(NullLogger<AgentRunWorker>.Instance);
        services.AddSingleton<AgentRunWorker>();

        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var worker = serviceProvider.GetRequiredService<AgentRunWorker>();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);

        var result = await mediator.Send(new CreateAgentRunCommand("writer", "Say hi"), cts.Token);

        Assert.Equal("writer", result.AgentCode);
        Assert.Equal("Say hi", result.Input);
        Assert.Equal("Running", result.Status);
        Assert.NotNull(result.RunId);

        var completedRun = await WaitForRunStatusAsync(agentRunRepo, result.RunId, "Completed");
        Assert.Equal("Hi from fake model.", completedRun.OutputPayload);
        Assert.Equal(2, completedRun.StatusVersion);

        var steps = await agentStepRepo.ListByRunIdAsync(result.RunId, CancellationToken.None);
        Assert.Collection(steps,
            step =>
            {
                Assert.Equal(1, step.StepNo);
                Assert.Equal("created", step.StepType);
                Assert.Equal("Completed", step.Status);
                Assert.Equal("Say hi", step.InputPayload);
            },
            step =>
            {
                Assert.Equal(2, step.StepNo);
                Assert.Equal("running", step.StepType);
                Assert.Equal("Completed", step.Status);
                Assert.Equal("Say hi", step.InputPayload);
            },
            step =>
            {
                Assert.Equal(3, step.StepNo);
                Assert.Equal("model_call", step.StepType);
                Assert.Equal("Completed", step.Status);
                Assert.Equal("Say hi", step.InputPayload);
                Assert.Equal("{\"action\":\"respond\",\"response\":\"Hi from fake model.\"}", step.OutputPayload);
            });

        Assert.Single(modelGateway.Requests);
        var request = modelGateway.Requests[0];
        Assert.Equal("gpt-4o", request.Model);
        Assert.Equal("Say hi", request.Input);
        Assert.Contains("You are a writer.", request.SystemPrompt);
        Assert.Contains("Return JSON only.", request.SystemPrompt);

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
                AllowedTools = "[]",
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

    private sealed class FakeModelGateway : IModelGateway
    {
        private readonly Queue<GenerateTextResult> _results;

        public FakeModelGateway(params GenerateTextResult[] results)
        {
            _results = new Queue<GenerateTextResult>(results);
        }

        public List<GenerateTextRequest> Requests { get; } = [];

        public Task<GenerateTextResult> GenerateTextAsync(GenerateTextRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No fake model result configured.");
            }

            return Task.FromResult(_results.Dequeue());
        }
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

    private sealed class InMemoryAgentApprovalRepository : IAgentApprovalRepository
    {
        public Task AddAsync(AgentApproval agentApproval, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AgentApproval?> GetByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken)
            => Task.FromResult<AgentApproval?>(null);

        public Task<IReadOnlyList<AgentApproval>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
            => Task.FromResult((IReadOnlyList<AgentApproval>)Array.Empty<AgentApproval>());

        public Task UpdateAsync(AgentApproval agentApproval, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
