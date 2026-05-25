namespace BestAgent.Domain.AgentRuns;

public interface IAgentStepRepository
{
    Task AddAsync(AgentStep agentStep, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentStep>> ListByRunIdAsync(string runId, CancellationToken cancellationToken);
}
