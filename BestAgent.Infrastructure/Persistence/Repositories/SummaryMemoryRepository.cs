using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class SummaryMemoryRepository : ISummaryMemoryRepository
{
    private readonly BestAgentDbContext _dbContext;

    public SummaryMemoryRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(SummaryMemory memory, CancellationToken cancellationToken)
    {
        await _dbContext.SummaryMemories.AddAsync(memory, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SummaryMemory?> GetLatestActiveAsync(
        string tenantId,
        string sessionId,
        string runId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var query = _dbContext.SummaryMemories
            .AsNoTracking()
            .Where(x =>
                !x.Deleted &&
                (x.ExpiresAt == null || x.ExpiresAt > now));

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(runId))
        {
            var byRun = await query
                .Where(x => x.RunId == runId)
                .OrderByDescending(x => x.GeneratedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (byRun is not null)
            {
                return byRun;
            }
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return await query
            .Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
