using BestAgent.Domain.Steps;

namespace BestAgent.Application.Abstractions;

public interface IAgentStepRepository
{
    Task AddAsync(AgentStep step, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentStep>> ListByRunIdAsync(string runId, CancellationToken cancellationToken);
}
