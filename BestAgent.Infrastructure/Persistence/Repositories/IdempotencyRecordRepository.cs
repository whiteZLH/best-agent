using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class IdempotencyRecordRepository : IIdempotencyRecordRepository
{
    private readonly BestAgentDbContext _dbContext;

    public IdempotencyRecordRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IdempotencyRecord?> GetByScopeAsync(string scopeType, string scopeKey, CancellationToken cancellationToken)
    {
        return _dbContext.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ScopeType == scopeType
                    && x.ScopeKey == scopeKey
                    && !x.Deleted,
                cancellationToken);
    }

    public async Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        await _dbContext.IdempotencyRecords.AddAsync(record, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
