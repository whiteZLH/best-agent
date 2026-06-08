namespace BestAgent.Domain.AgentRuns;

public interface IIdempotencyRecordRepository
{
    Task<IdempotencyRecord?> GetByScopeAsync(string scopeType, string scopeKey, CancellationToken cancellationToken);

    Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken);
}
