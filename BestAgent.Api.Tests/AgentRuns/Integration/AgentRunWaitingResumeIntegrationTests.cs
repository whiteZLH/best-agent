using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Runtime;
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

namespace BestAgent.Api.Tests.AgentRuns.Integration;

public class AgentRunWaitingResumeIntegrationTests
{
    [Fact]
    public async Task ShouldCreateRun_WaitForApproval_Approve_AndExposeFinalRunAndSteps()
    {
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var agentRunRepo = new InMemoryAgentRunRepository();
        var agentStepRepo = new InMemoryAgentStepRepository();
        var agentApprovalRepo = new InMemoryAgentApprovalRepository();
        var toolInvocationRepo = new InMemoryToolInvocationRepository();
        var eventBus = new RecordingEventBus();
        var channel = new AgentRunChannel();
        var resolvedDefinition = CreateResolvedDefinition();

        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(resolvedDefinition);

        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("{\"action\":\"tool_call\",\"toolName\":\"weather\",\"toolInput\":\"{\\\"city\\\":\\\"Shanghai\\\"}\"}"),
                new GenerateTextResult("{\"action\":\"respond\",\"response\":\"The weather is sunny.\"}"));

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

        var waitingRun = await WaitForRunStatusAsync(agentRunRepo, createResult.RunId, "WaitingApproval");
        Assert.False(string.IsNullOrWhiteSpace(waitingRun.CurrentWaitToken));
        Assert.Equal(4, waitingRun.CurrentStepNo);

        var waitingSnapshot = await mediator.Send(new GetAgentRunByIdQuery(createResult.RunId), cts.Token);
        Assert.NotNull(waitingSnapshot);
        Assert.Equal("WaitingApproval", waitingSnapshot!.Status);

        var waitingSteps = await mediator.Send(new GetAgentRunStepsQuery(createResult.RunId), cts.Token);
        var pendingStep = Assert.Single(waitingSteps, step => step.StepType == "tool_call" && step.Status == "Pending");
        Assert.NotNull(pendingStep.Approval);
        Assert.Equal("approval", pendingStep.Approval!.WaitType);
        Assert.Equal("weather", pendingStep.Approval.ToolName);
        Assert.Equal("Pending", pendingStep.Approval.Decision);

        var waitingApprovals = await mediator.Send(new GetAgentRunApprovalsQuery(createResult.RunId), cts.Token);
        var waitingApproval = Assert.Single(waitingApprovals);
        Assert.Equal("Pending", waitingApproval.Decision);
        Assert.Equal(pendingStep.StepId, waitingApproval.StepId);

        var approveResult = await mediator.Send(new ApproveAgentRunStepCommand(createResult.RunId, pendingStep.StepId, "u-1", "Alice", "admin", "Looks good"), cts.Token);
        Assert.Equal("Running", approveResult.Status);

        var completedRun = await WaitForRunStatusAsync(agentRunRepo, createResult.RunId, "Completed");
        Assert.Equal("The weather is sunny.", completedRun.OutputPayload);

        var completedSnapshot = await mediator.Send(new GetAgentRunByIdQuery(createResult.RunId), cts.Token);
        Assert.NotNull(completedSnapshot);
        Assert.Equal("Completed", completedSnapshot!.Status);
        Assert.Equal("The weather is sunny.", completedSnapshot.Output);

        var completedSteps = await mediator.Send(new GetAgentRunStepsQuery(createResult.RunId), cts.Token);
        Assert.Contains(completedSteps, step =>
            step.StepType == "tool_call" &&
            step.Status == "Completed" &&
            step.Output == "sunny" &&
            step.ToolInvocation != null &&
            step.ToolInvocation.Status == "Completed" &&
            step.ToolInvocation.Mode == "sync" &&
            step.Approval != null &&
            step.Approval.Decision == "Approved" &&
            step.Approval.ApproverName == "Alice");
        Assert.Contains(completedSteps, step => step.StepType == "model_call" && step.Status == "Completed" && step.Output == "{\"action\":\"respond\",\"response\":\"The weather is sunny.\"}");

        var completedApprovals = await mediator.Send(new GetAgentRunApprovalsQuery(createResult.RunId), cts.Token);
        var completedApproval = Assert.Single(completedApprovals);
        Assert.Equal("Approved", completedApproval.Decision);
        Assert.Equal("Alice", completedApproval.ApproverName);
        Assert.Equal("Looks good", completedApproval.Comment);

        Assert.Contains(eventBus.Events, evt => evt.EventType == "waiting_approval" && evt.Data.Status == "Pending");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "done" && evt.Data.Output == "The weather is sunny.");

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ShouldCreateRun_WaitForApproval_Reject_AndFailRun()
    {
        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var modelGateway = Substitute.For<IModelGateway>();
        var toolExecutor = Substitute.For<IToolExecutor>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var agentRunRepo = new InMemoryAgentRunRepository();
        var agentStepRepo = new InMemoryAgentStepRepository();
        var agentApprovalRepo = new InMemoryAgentApprovalRepository();
        var toolInvocationRepo = new InMemoryToolInvocationRepository();
        var eventBus = new RecordingEventBus();
        var channel = new AgentRunChannel();
        var resolvedDefinition = CreateResolvedDefinition();

        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(resolvedDefinition);

        modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("{\"action\":\"tool_call\",\"toolName\":\"weather\",\"toolInput\":\"{\\\"city\\\":\\\"Shanghai\\\"}\"}"));

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
        var waitingRun = await WaitForRunStatusAsync(agentRunRepo, createResult.RunId, "WaitingApproval");
        Assert.False(string.IsNullOrWhiteSpace(waitingRun.CurrentWaitToken));

        var waitingSteps = await mediator.Send(new GetAgentRunStepsQuery(createResult.RunId), cts.Token);
        var pendingStep = Assert.Single(waitingSteps, step => step.StepType == "tool_call" && step.Status == "Pending");

        var rejectResult = await mediator.Send(new RejectAgentRunStepCommand(createResult.RunId, pendingStep.StepId, "Denied", "u-1", "Alice", "admin"), cts.Token);
        Assert.Equal("Running", rejectResult.Status);

        var failedRun = await WaitForRunStatusAsync(agentRunRepo, createResult.RunId, "Failed");
        Assert.Equal("Denied", failedRun.InterruptReason);

        var failedSteps = await mediator.Send(new GetAgentRunStepsQuery(createResult.RunId), cts.Token);
        Assert.Contains(failedSteps, step =>
            step.StepType == "tool_call" &&
            step.Status == "Failed" &&
            step.Approval != null &&
            step.Approval.Decision == "Rejected" &&
            step.Approval.Comment == "Denied" &&
            step.Approval.ApproverName == "Alice");

        var failedApprovals = await mediator.Send(new GetAgentRunApprovalsQuery(createResult.RunId), cts.Token);
        var failedApproval = Assert.Single(failedApprovals);
        Assert.Equal("Rejected", failedApproval.Decision);
        Assert.Equal("Denied", failedApproval.Comment);
        Assert.Equal("Alice", failedApproval.ApproverName);

        await toolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>());

        Assert.Contains(eventBus.Events, evt => evt.EventType == "approval_rejected");
        Assert.Contains(eventBus.Events, evt => evt.EventType == "error" && evt.Data.Error == "Denied");

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
        private readonly List<AgentApproval> _approvals = [];
        private readonly object _lock = new();

        public Task AddAsync(AgentApproval agentApproval, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _approvals.Add(agentApproval);
            }

            return Task.CompletedTask;
        }

        public Task<AgentApproval?> GetByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult(_approvals.FirstOrDefault(x => x.RunId == runId && x.StepId == stepId && !x.Deleted));
            }
        }

        public Task<AgentApproval?> GetByApprovalIdAsync(string approvalId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult(_approvals.FirstOrDefault(x => x.ApprovalId == approvalId && !x.Deleted));
            }
        }

        public Task<IReadOnlyList<AgentApproval>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult((IReadOnlyList<AgentApproval>)_approvals
                    .Where(x => x.RunId == runId && !x.Deleted)
                    .OrderBy(x => x.CreateTime)
                    .ToList());
            }
        }

        public Task<IReadOnlyList<AgentApproval>> ListExpiredPendingAsync(DateTime utcNow, int limit, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult((IReadOnlyList<AgentApproval>)_approvals
                    .Where(x =>
                        !x.Deleted &&
                        x.ExpiresAt != null &&
                        x.ExpiresAt <= utcNow &&
                        x.Decision == "Pending")
                    .OrderBy(x => x.ExpiresAt)
                    .ThenBy(x => x.CreateTime)
                    .Take(limit)
                    .ToList());
            }
        }

        public Task UpdateAsync(AgentApproval agentApproval, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                var index = _approvals.FindIndex(x => x.ApprovalId == agentApproval.ApprovalId);
                if (index >= 0)
                {
                    _approvals[index] = agentApproval;
                }
            }

            return Task.CompletedTask;
        }
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
