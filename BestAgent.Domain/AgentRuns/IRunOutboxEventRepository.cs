namespace BestAgent.Domain.AgentRuns;

public interface IRunOutboxEventRepository
{
    Task AddAsync(RunOutboxEvent outboxEvent, CancellationToken cancellationToken);

    Task<IReadOnlyList<RunOutboxEvent>> ListByRunIdAsync(string runId, long? afterSeqNo, CancellationToken cancellationToken);

    Task<IReadOnlyList<RunOutboxEvent>> ListPendingAsync(int limit, CancellationToken cancellationToken);

    Task<long> GetNextSeqNoAsync(string runId, CancellationToken cancellationToken);

    Task MarkPublishedAsync(string eventId, DateTime publishedAt, CancellationToken cancellationToken);

    Task MarkFailedAsync(string eventId, CancellationToken cancellationToken);
}
