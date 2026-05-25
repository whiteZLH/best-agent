using BestAgent.Domain.Runs;

namespace BestAgent.Application.Abstractions;

public interface IAgentRunRepository
{
    Task<AgentRun?> GetByIdAsync(string runId, CancellationToken cancellationToken);

    Task<AgentRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task AddAsync(AgentRun run, CancellationToken cancellationToken);
}
