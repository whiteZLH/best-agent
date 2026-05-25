using BestAgent.Application.Abstractions;
using BestAgent.Domain.Idempotency;
using BestAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Repositories;

internal sealed class IdempotencyRecordRepository : IIdempotencyRecordRepository
{
    private readonly BestAgentDbContext _dbContext;

    public IdempotencyRecordRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IdempotencyRecord?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return _dbContext.IdempotencyRecords.SingleOrDefaultAsync(entity => entity.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        return _dbContext.IdempotencyRecords.AddAsync(record, cancellationToken).AsTask();
    }
}
