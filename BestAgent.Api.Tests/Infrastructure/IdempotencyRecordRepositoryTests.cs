using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class IdempotencyRecordRepositoryTests
{
    [Fact]
    public async Task ShouldAddAndGetByScope()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new IdempotencyRecordRepository(dbContext);

            await repository.AddAsync(
                CreateRecord("record-1", "tool_complete", "scope-1", "invocation-1", now),
                CancellationToken.None);
            await repository.AddAsync(
                CreateRecord("record-2", "approval_complete", "scope-2", "approval-1", now.AddSeconds(1)),
                CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new IdempotencyRecordRepository(dbContext);

            var toolRecord = await repository.GetByScopeAsync("tool_complete", "scope-1", CancellationToken.None);
            var approvalRecord = await repository.GetByScopeAsync("approval_complete", "scope-2", CancellationToken.None);
            var missingRecord = await repository.GetByScopeAsync("tool_complete", "missing", CancellationToken.None);

            Assert.NotNull(toolRecord);
            Assert.Equal("record-1", toolRecord.Id);
            Assert.Equal("invocation-1", toolRecord.TargetId);
            Assert.Equal("{\"status\":\"Running\"}", toolRecord.ExtraPayload);
            Assert.NotNull(approvalRecord);
            Assert.Equal("record-2", approvalRecord.Id);
            Assert.Null(missingRecord);
        }
    }

    [Fact]
    public async Task GetByScope_ShouldIgnoreDeletedRecords()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            await dbContext.IdempotencyRecords.AddAsync(
                CreateRecord("record-1", "tool_complete", "scope-1", "invocation-1", now) with
                {
                    Deleted = true
                });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new IdempotencyRecordRepository(dbContext);

            var record = await repository.GetByScopeAsync("tool_complete", "scope-1", CancellationToken.None);

            Assert.Null(record);
        }
    }

    private static IdempotencyRecord CreateRecord(
        string id,
        string scopeType,
        string scopeKey,
        string targetId,
        DateTime now)
    {
        return new IdempotencyRecord
        {
            Id = id,
            ScopeType = scopeType,
            ScopeKey = scopeKey,
            RequestHash = $"hash-{id}",
            TargetId = targetId,
            Status = "completed",
            ExpireAt = now.AddDays(7),
            ExtraPayload = "{\"status\":\"Running\"}",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }
}
