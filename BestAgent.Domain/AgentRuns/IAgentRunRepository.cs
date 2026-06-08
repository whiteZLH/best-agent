namespace BestAgent.Domain.AgentRuns;

public interface IAgentRunRepository
{
    Task AddAsync(AgentRun agentRun, CancellationToken cancellationToken);

    Task<AgentRun?> GetByRunIdAsync(string runId, CancellationToken cancellationToken);

    Task<AgentRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentRun>> ListByParentRunIdAsync(string parentRunId, CancellationToken cancellationToken);

    Task<AgentRun?> GetLatestChildByParentRunIdAsync(string parentRunId, CancellationToken cancellationToken);

    Task UpdateAsync(AgentRun agentRun, CancellationToken cancellationToken);
}
