using BestAgent.Domain.Idempotency;

namespace BestAgent.Application.Abstractions;

public interface IIdempotencyRecordRepository
{
    Task<IdempotencyRecord?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken);
}
