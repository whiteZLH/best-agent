using BestAgent.Application.Abstractions;
using BestAgent.Application.Planning;
using BestAgent.Domain.Agents;
using BestAgent.Domain.Events;
using BestAgent.Domain.Idempotency;
using BestAgent.Domain.Messages;
using BestAgent.Domain.Runs;
using BestAgent.Domain.Steps;
using BestAgent.Domain.Tools;

namespace BestAgent.UnitTests.Application;

internal sealed class InMemoryRuntimeDependencies :
    IAgentDefinitionRepository,
    IAgentRunRepository,
    IAgentStepRepository,
    IAgentMessageRepository,
    IIdempotencyRecordRepository,
    IOutboxEventRepository,
    IToolInvocationRepository,
    IUnitOfWork,
    IToolRegistry,
    IToolExecutor,
    IModelGateway
{
    private readonly Queue<Func<PlanDecision>> _planQueue = new();

    public List<AgentDefinition> Definitions { get; } = [];
    public List<AgentRun> Runs { get; } = [];
    public List<AgentStep> Steps { get; } = [];
    public List<AgentMessage> Messages { get; } = [];
    public List<IdempotencyRecord> IdempotencyRecords { get; } = [];
    public List<OutboxEvent> OutboxEvents { get; } = [];
    public List<ToolInvocation> ToolInvocations { get; } = [];
    public HashSet<string> RegisteredTools { get; } = ["echo_context"];

    public void QueuePlan(PlanDecision decision)
    {
        _planQueue.Enqueue(() => decision);
    }

    public void QueuePlanException(Exception exception)
    {
        _planQueue.Enqueue(() => throw exception);
    }

    public Task<AgentDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        return Task.FromResult(Definitions.SingleOrDefault(item => item.Code == code && item.Enabled));
    }

    public Task<AgentRun?> GetByIdAsync(string runId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Runs.SingleOrDefault(item => item.RunId == runId));
    }

    public Task<AgentRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return Task.FromResult(Runs.SingleOrDefault(item => item.IdempotencyKey == idempotencyKey));
    }

    public Task AddAsync(AgentRun run, CancellationToken cancellationToken)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task AddAsync(AgentStep step, CancellationToken cancellationToken)
    {
        Steps.Add(step);
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<AgentStep>> IAgentStepRepository.ListByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AgentStep>>(Steps.Where(item => item.RunId == runId).OrderBy(item => item.StepNo).ToList());
    }

    public Task AddAsync(AgentMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<AgentMessage>> IAgentMessageRepository.ListByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AgentMessage>>(Messages.Where(item => item.RunId == runId).OrderBy(item => item.CreatedAt).ToList());
    }

    public Task<IdempotencyRecord?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return Task.FromResult(IdempotencyRecords.SingleOrDefault(item => item.IdempotencyKey == idempotencyKey));
    }

    public Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        IdempotencyRecords.Add(record);
        return Task.CompletedTask;
    }

    public Task<int> GetNextSequenceAsync(string runId, CancellationToken cancellationToken)
    {
        return Task.FromResult(OutboxEvents.Count(item => item.RunId == runId) + 1);
    }

    public Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        OutboxEvents.Add(outboxEvent);
        return Task.CompletedTask;
    }

    public Task AddAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        ToolInvocations.Add(invocation);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(1);
    }

    public ToolDefinition? Get(string toolName)
    {
        return RegisteredTools.Contains(toolName)
            ? new ToolDefinition { ToolName = toolName, Description = "test tool", Enabled = true }
            : null;
    }

    public Task<ToolExecutionResult> ExecuteAsync(string toolName, string argumentsJson, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolExecutionResult("succeeded", """{"echoedText":"ok"}""", null, """{"durationMs":0}"""));
    }

    public Task<PlanDecision> PlanAsync(ModelContext context, CancellationToken cancellationToken)
    {
        if (_planQueue.Count == 0)
        {
            throw new InvalidOperationException("No queued plan decision.");
        }

        return Task.FromResult(_planQueue.Dequeue().Invoke());
    }
}
