using BestAgent.Application.Abstractions;
using BestAgent.Domain.Events;
using BestAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Repositories;

internal sealed class OutboxEventRepository : IOutboxEventRepository
{
    private readonly BestAgentDbContext _dbContext;

    public OutboxEventRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> GetNextSequenceAsync(string runId, CancellationToken cancellationToken)
    {
        var current = await _dbContext.OutboxEvents
            .Where(entity => entity.RunId == runId)
            .Select(entity => (int?)entity.SequenceNo)
            .MaxAsync(cancellationToken);

        return (current ?? 0) + 1;
    }

    public Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        return _dbContext.OutboxEvents.AddAsync(outboxEvent, cancellationToken).AsTask();
    }
}
