using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class UserMemoryRepository : IUserMemoryRepository
{
    private readonly BestAgentDbContext _dbContext;

    public UserMemoryRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(UserMemory memory, CancellationToken cancellationToken)
    {
        await _dbContext.UserMemories.AddAsync(memory, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(UserMemory memory, CancellationToken cancellationToken)
    {
        _dbContext.UserMemories.Update(memory);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<UserMemory?> GetByMemoryKeyAsync(
        string tenantId,
        string userId,
        string memoryKey,
        CancellationToken cancellationToken)
    {
        return _dbContext.UserMemories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId
                     && x.UserId == userId
                     && x.MemoryKey == memoryKey
                     && !x.Deleted,
                cancellationToken);
    }

    public async Task<IReadOnlyList<UserMemory>> ListActiveByUserAsync(
        string tenantId,
        string userId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || maxCount <= 0)
        {
            return Array.Empty<UserMemory>();
        }

        var now = DateTime.UtcNow;
        return await _dbContext.UserMemories
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.UserId == userId &&
                !x.Deleted &&
                (x.EffectiveAt == null || x.EffectiveAt <= now) &&
                (x.ExpiresAt == null || x.ExpiresAt > now))
            .OrderByDescending(x => x.EffectiveAt ?? x.CreateTime)
            .Take(maxCount)
            .ToArrayAsync(cancellationToken);
    }
}
