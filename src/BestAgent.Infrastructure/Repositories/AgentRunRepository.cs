using BestAgent.Application.Abstractions;
using BestAgent.Domain.Runs;
using BestAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Repositories;

internal sealed class AgentRunRepository : IAgentRunRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentRunRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AgentRun?> GetByIdAsync(string runId, CancellationToken cancellationToken)
    {
        return _dbContext.AgentRuns.SingleOrDefaultAsync(entity => entity.RunId == runId, cancellationToken);
    }

    public Task<AgentRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return _dbContext.AgentRuns.SingleOrDefaultAsync(entity => entity.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public Task AddAsync(AgentRun run, CancellationToken cancellationToken)
    {
        return _dbContext.AgentRuns.AddAsync(run, cancellationToken).AsTask();
    }
}
