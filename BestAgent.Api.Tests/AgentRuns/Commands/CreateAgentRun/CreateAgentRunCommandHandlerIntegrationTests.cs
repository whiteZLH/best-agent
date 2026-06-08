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
        var toolInvocationRepo = new InMemoryToolInvocationRepository();
        var modelGateway = new FakeModelGateway(
            new GenerateTextResult("{\"action\":\"respond\",\"response\":\"Hi from fake model.\"}"));
        var toolExecutor = Substitute.For<IToolExecutor>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var resolvedDefinition = CreateResolvedDefinition();

        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(resolvedDefinition);
        toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Description = "Get the weather for a city",
                InputSchema = "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"],\"additionalProperties\":false}",
                Enabled = true,
                SideEffectLevel = "read_only"
            });

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IAgentDefinitionRepository>(agentDefinitionRepo);
        services.AddSingleton<IAgentRunRepository>(agentRunRepo);
        services.AddSingleton<IAgentStepRepository>(agentStepRepo);
        services.AddSingleton<IAgentApprovalRepository>(agentApprovalRepo);
        services.AddSingleton<IToolInvocationRepository>(toolInvocationRepo);
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

        var result = await mediator.Send(new CreateAgentRunCommand("writer", "Say hi", null, "tenant-1", "user-1", "session-1"), cts.Token);

        Assert.Equal("writer", result.AgentCode);
        Assert.Equal("Say hi", result.Input);
        Assert.Equal("Running", result.Status);
        Assert.NotNull(result.RunId);

        var completedRun = await WaitForRunStatusAsync(agentRunRepo, result.RunId, "Completed");
        Assert.Equal("Hi from fake model.", completedRun.OutputPayload);
        Assert.Equal(2, completedRun.StatusVersion);
        Assert.Equal("tenant-1", completedRun.TenantId);
        Assert.Equal("user-1", completedRun.UserId);
        Assert.Equal("session-1", completedRun.SessionId);
        Assert.Equal(result.RunId, completedRun.RootRunId);
        Assert.Equal(string.Empty, completedRun.ParentRunId);
        Assert.Equal(string.Empty, completedRun.DelegatedByRunId);
        Assert.Equal(string.Empty, completedRun.DelegatedByAgent);

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
        Assert.NotNull(request.Tools);
        var tool = Assert.Single(request.Tools!);
        Assert.Equal("weather", tool.Name);
        Assert.Equal("Get the weather for a city", tool.Description);
        Assert.Equal("{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"],\"additionalProperties\":false}", tool.InputSchema);
        Assert.Equal("auto", request.ToolChoice);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_WithMaxRoundsOverride_ShouldClampRunAndFailWhenTurnBudgetIsConsumed()
    {
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var agentRunRepo = new InMemoryAgentRunRepository();
        var agentStepRepo = new InMemoryAgentStepRepository();
        var agentApprovalRepo = new InMemoryAgentApprovalRepository();
        var toolInvocationRepo = new InMemoryToolInvocationRepository();
        var modelGateway = new FakeModelGateway(
            new GenerateTextResult("{\"action\":\"tool_call\",\"toolName\":\"weather\",\"toolInput\":\"{\\\"city\\\":\\\"Shanghai\\\"}\"}"),
            new GenerateTextResult("{\"action\":\"respond\",\"response\":\"This response should never be used.\"}"));
        var toolExecutor = Substitute.For<IToolExecutor>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var resolvedDefinition = CreateResolvedDefinition();

        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(resolvedDefinition);
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
        toolExecutor.ExecuteAsync(
                "weather",
                "{\"city\":\"Shanghai\"}",
                Arg.Any<ToolExecutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "sunny"));

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IAgentDefinitionRepository>(agentDefinitionRepo);
        services.AddSingleton<IAgentRunRepository>(agentRunRepo);
        services.AddSingleton<IAgentStepRepository>(agentStepRepo);
        services.AddSingleton<IAgentApprovalRepository>(agentApprovalRepo);
        services.AddSingleton<IToolInvocationRepository>(toolInvocationRepo);
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

        var result = await mediator.Send(
            new CreateAgentRunCommand("writer", "Say hi", null, "tenant-1", "user-1", "session-1", null, 1),
            cts.Token);

        var failedRun = await WaitForRunStatusAsync(agentRunRepo, result.RunId, "Failed");
        Assert.Equal(1, failedRun.MaxTurns);
        Assert.Equal("Max turns exceeded.", failedRun.InterruptReason);

        var steps = await agentStepRepo.ListByRunIdAsync(result.RunId, CancellationToken.None);
        Assert.Contains(steps, step =>
            step.StepType == "tool_call" &&
            step.Status == "Completed" &&
            step.OutputPayload == "sunny");
        Assert.Contains(steps, step =>
            step.StepType == "failed" &&
            step.Status == "Failed" &&
            step.ErrorPayload == "Max turns exceeded.");
        Assert.Single(modelGateway.Requests);

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

        public Task<AgentRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                var run = _runs.Values.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
                return Task.FromResult(run);
            }
        }

        public Task<IReadOnlyList<AgentRun>> ListByParentRunIdAsync(string parentRunId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult((IReadOnlyList<AgentRun>)_runs.Values
                    .Where(x => x.ParentRunId == parentRunId)
                    .OrderBy(x => x.CreateTime)
                    .ToList());
            }
        }

        public Task<AgentRun?> GetLatestChildByParentRunIdAsync(string parentRunId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult(_runs.Values
                    .Where(x => x.ParentRunId == parentRunId)
                    .OrderByDescending(x => x.CreateTime)
                    .ThenByDescending(x => x.RunId)
                    .FirstOrDefault());
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

        public Task<AgentStep?> GetByStepIdAsync(string stepId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult(_steps.FirstOrDefault(step => step.StepId == stepId));
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

        public Task<AgentApproval?> GetByApprovalIdAsync(string approvalId, CancellationToken cancellationToken)
            => Task.FromResult<AgentApproval?>(null);

        public Task<AgentApproval?> GetByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken)
            => Task.FromResult<AgentApproval?>(null);

        public Task<IReadOnlyList<AgentApproval>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
            => Task.FromResult((IReadOnlyList<AgentApproval>)Array.Empty<AgentApproval>());

        public Task<IReadOnlyList<AgentApproval>> ListExpiredPendingAsync(DateTime utcNow, int limit, CancellationToken cancellationToken)
            => Task.FromResult((IReadOnlyList<AgentApproval>)Array.Empty<AgentApproval>());

        public Task UpdateAsync(AgentApproval agentApproval, CancellationToken cancellationToken) => Task.CompletedTask;
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
}
