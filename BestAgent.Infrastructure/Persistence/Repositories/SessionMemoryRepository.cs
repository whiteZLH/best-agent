using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class SessionMemoryRepository : ISessionMemoryRepository
{
    private readonly BestAgentDbContext _dbContext;

    public SessionMemoryRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(SessionMemory memory, CancellationToken cancellationToken)
    {
        await _dbContext.SessionMemories.AddAsync(memory, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionMemory>> ListActiveBySessionAsync(
        string tenantId,
        string sessionId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || maxCount <= 0)
        {
            return Array.Empty<SessionMemory>();
        }

        var now = DateTime.UtcNow;
        var query = _dbContext.SessionMemories
            .AsNoTracking()
            .Where(x =>
                x.SessionId == sessionId &&
                !x.Deleted &&
                (x.ExpiresAt == null || x.ExpiresAt > now));

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        return await query
            .OrderByDescending(x => x.CreateTime)
            .Take(maxCount)
            .ToArrayAsync(cancellationToken);
    }
}
