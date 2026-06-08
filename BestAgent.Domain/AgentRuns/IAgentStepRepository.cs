namespace BestAgent.Domain.AgentRuns;

public interface IAgentStepRepository
{
    Task AddAsync(AgentStep agentStep, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentStep>> ListByRunIdAsync(string runId, CancellationToken cancellationToken);

    Task<AgentStep?> GetLastByRunIdAsync(string runId, CancellationToken cancellationToken);

    Task<AgentStep?> GetByStepIdAsync(string stepId, CancellationToken cancellationToken);

    Task UpdateAsync(AgentStep agentStep, CancellationToken cancellationToken);
}
