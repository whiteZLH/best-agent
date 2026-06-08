using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class RunOutboxEventRepositoryTests
{
    [Fact]
    public async Task ShouldAddListSequenceAndUpdatePublishStatus()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 6, 10, 30, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new RunOutboxEventRepository(dbContext);

            await repository.AddAsync(CreateEvent("event-1", "run-1", 1, "run.started", "Running", now), CancellationToken.None);
            await repository.AddAsync(CreateEvent("event-2", "run-1", 2, "run.completed", "Completed", now.AddSeconds(1)), CancellationToken.None);
            await repository.AddAsync(CreateEvent("event-3", "run-2", 1, "run.started", "Running", now.AddSeconds(2)), CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new RunOutboxEventRepository(dbContext);

            var runEvents = await repository.ListByRunIdAsync("run-1", null, CancellationToken.None);
            var replayAfterFirst = await repository.ListByRunIdAsync("run-1", 1, CancellationToken.None);
            var pending = await repository.ListPendingAsync(2, CancellationToken.None);
            var nextSeqNo = await repository.GetNextSeqNoAsync("run-1", CancellationToken.None);
            var firstSeqNo = await repository.GetNextSeqNoAsync("new-run", CancellationToken.None);

            Assert.Equal(["event-1", "event-2"], runEvents.Select(x => x.EventId));
            Assert.Equal(["event-2"], replayAfterFirst.Select(x => x.EventId));
            Assert.Equal(["event-1", "event-2"], pending.Select(x => x.EventId));
            Assert.Equal(3, nextSeqNo);
            Assert.Equal(1, firstSeqNo);

            await repository.MarkPublishedAsync("event-1", now.AddMinutes(1), CancellationToken.None);
            await repository.MarkRetryPendingAsync("event-2", CancellationToken.None);
            await repository.MarkDeadAsync("event-3", CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var published = await dbContext.RunOutboxEvents.SingleAsync(x => x.EventId == "event-1");
            var retryPending = await dbContext.RunOutboxEvents.SingleAsync(x => x.EventId == "event-2");
            var failed = await dbContext.RunOutboxEvents.SingleAsync(x => x.EventId == "event-3");
            var pending = await new RunOutboxEventRepository(dbContext).ListPendingAsync(10, CancellationToken.None);

            Assert.Equal("published", published.PublishStatus);
            Assert.Equal(now.AddMinutes(1), published.PublishedAt);
            Assert.Equal("pending", retryPending.PublishStatus);
            Assert.Equal(1, retryPending.RetryCount);
            Assert.Equal("failed", failed.PublishStatus);
            Assert.Equal(1, failed.RetryCount);
            Assert.DoesNotContain(pending, x => x.EventId is "event-1" or "event-3");
            Assert.Contains(pending, x => x.EventId == "event-2");
        }
    }

    private static RunOutboxEvent CreateEvent(
        string eventId,
        string runId,
        long seqNo,
        string eventType,
        string runStatus,
        DateTime occurredAt)
    {
        return new RunOutboxEvent
        {
            EventId = eventId,
            RunId = runId,
            SeqNo = seqNo,
            EventType = eventType,
            RunStatus = runStatus,
            Payload = "{\"value\":1}",
            PublishStatus = "pending",
            OccurredAt = occurredAt,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = occurredAt,
            LastModifyTime = occurredAt
        };
    }
}
