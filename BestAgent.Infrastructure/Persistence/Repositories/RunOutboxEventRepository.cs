using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class RunOutboxEventRepository : IRunOutboxEventRepository
{
    private readonly BestAgentDbContext _dbContext;

    public RunOutboxEventRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(RunOutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        await _dbContext.RunOutboxEvents.AddAsync(outboxEvent, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RunOutboxEvent>> ListByRunIdAsync(string runId, long? afterSeqNo, CancellationToken cancellationToken)
    {
        var query = _dbContext.RunOutboxEvents
            .AsNoTracking()
            .Where(x => x.RunId == runId && !x.Deleted);

        if (afterSeqNo.HasValue)
        {
            query = query.Where(x => x.SeqNo > afterSeqNo.Value);
        }

        return await query
            .OrderBy(x => x.SeqNo)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RunOutboxEvent>> ListPendingAsync(int limit, CancellationToken cancellationToken)
    {
        var take = limit <= 0 ? 100 : limit;
        return await _dbContext.RunOutboxEvents
            .AsNoTracking()
            .Where(x => x.PublishStatus == "pending" && !x.Deleted)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.SeqNo)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> GetNextSeqNoAsync(string runId, CancellationToken cancellationToken)
    {
        var maxSeqNo = await _dbContext.RunOutboxEvents
            .AsNoTracking()
            .Where(x => x.RunId == runId && !x.Deleted)
            .Select(x => (long?)x.SeqNo)
            .MaxAsync(cancellationToken);

        return (maxSeqNo ?? 0) + 1;
    }

    public async Task MarkPublishedAsync(string eventId, DateTime publishedAt, CancellationToken cancellationToken)
    {
        var outboxEvent = await _dbContext.RunOutboxEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EventId == eventId && !x.Deleted, cancellationToken);
        if (outboxEvent is null)
        {
            return;
        }

        outboxEvent = outboxEvent with
        {
            PublishStatus = "published",
            PublishedAt = publishedAt,
            LastModifyTime = publishedAt
        };
        _dbContext.RunOutboxEvents.Update(outboxEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRetryPendingAsync(string eventId, CancellationToken cancellationToken)
    {
        var outboxEvent = await _dbContext.RunOutboxEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EventId == eventId && !x.Deleted, cancellationToken);
        if (outboxEvent is null)
        {
            return;
        }

        var failedAt = DateTime.UtcNow;
        outboxEvent = outboxEvent with
        {
            PublishStatus = "pending",
            RetryCount = outboxEvent.RetryCount + 1,
            LastModifyTime = failedAt
        };
        _dbContext.RunOutboxEvents.Update(outboxEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkDeadAsync(string eventId, CancellationToken cancellationToken)
    {
        var outboxEvent = await _dbContext.RunOutboxEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EventId == eventId && !x.Deleted, cancellationToken);
        if (outboxEvent is null)
        {
            return;
        }

        var failedAt = DateTime.UtcNow;
        outboxEvent = outboxEvent with
        {
            PublishStatus = "failed",
            RetryCount = outboxEvent.RetryCount + 1,
            LastModifyTime = failedAt
        };
        _dbContext.RunOutboxEvents.Update(outboxEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
