using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class AgentRunRepository : IAgentRunRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentRunRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AgentRun agentRun, CancellationToken cancellationToken)
    {
        await _dbContext.AgentRuns.AddAsync(agentRun, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<AgentRun?> GetByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return _dbContext.AgentRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.RunId == runId && !x.Deleted, cancellationToken);
    }

    public Task<AgentRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return _dbContext.AgentRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey && !x.Deleted, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRun>> ListByParentRunIdAsync(string parentRunId, CancellationToken cancellationToken)
    {
        return await _dbContext.AgentRuns
            .AsNoTracking()
            .Where(x => x.ParentRunId == parentRunId && !x.Deleted)
            .OrderBy(x => x.CreateTime)
            .ToListAsync(cancellationToken);
    }

    public Task<AgentRun?> GetLatestChildByParentRunIdAsync(string parentRunId, CancellationToken cancellationToken)
    {
        return _dbContext.AgentRuns
            .AsNoTracking()
            .Where(x => x.ParentRunId == parentRunId && !x.Deleted)
            .OrderByDescending(x => x.CreateTime)
            .ThenByDescending(x => x.RunId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateAsync(AgentRun agentRun, CancellationToken cancellationToken)
    {
        _dbContext.AgentRuns.Update(agentRun);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
