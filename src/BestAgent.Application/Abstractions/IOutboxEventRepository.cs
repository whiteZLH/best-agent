using BestAgent.Domain.Events;

namespace BestAgent.Application.Abstractions;

public interface IOutboxEventRepository
{
    Task<int> GetNextSequenceAsync(string runId, CancellationToken cancellationToken);

    Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}
