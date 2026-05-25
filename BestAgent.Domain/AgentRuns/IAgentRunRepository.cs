namespace BestAgent.Domain.AgentRuns;

public interface IAgentRunRepository
{
    Task AddAsync(AgentRun agentRun, CancellationToken cancellationToken);
}
