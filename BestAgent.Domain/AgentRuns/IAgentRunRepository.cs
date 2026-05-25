namespace BestAgent.Domain.AgentRuns;

public interface IAgentRunRepository
{
    Task AddAsync(AgentRun agentRun, CancellationToken cancellationToken);

    Task<AgentRun?> GetByRunIdAsync(string runId, CancellationToken cancellationToken);

    Task UpdateAsync(AgentRun agentRun, CancellationToken cancellationToken);
}
