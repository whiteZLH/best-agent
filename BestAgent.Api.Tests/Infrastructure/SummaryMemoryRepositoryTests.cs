using BestAgent.Domain.Knowledge;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class SummaryMemoryRepositoryTests
{
    [Fact]
    public async Task GetLatestActiveAsync_ShouldSkipExpiredRunSummary_AndFallbackToSessionSummary()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = DateTime.UtcNow;

        await using (var dbContext = new BestAgentDbContext(options))
        {
            dbContext.SummaryMemories.AddRange(
                new SummaryMemory
                {
                    Id = "summary-expired-run",
                    TenantId = "tenant-1",
                    RunId = "run-1",
                    SessionId = "session-1",
                    SummaryType = "run_completion",
                    SourceStartSeq = 1,
                    SourceEndSeq = 3,
                    SummaryText = "Expired run summary",
                    GeneratedByModel = "runtime_template",
                    GeneratedAt = now.AddMinutes(-20),
                    ExpiresAt = now.AddMinutes(-1),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now.AddMinutes(-20),
                    LastModifyTime = now.AddMinutes(-20)
                },
                new SummaryMemory
                {
                    Id = "summary-active-session",
                    TenantId = "tenant-1",
                    RunId = "run-0",
                    SessionId = "session-1",
                    SummaryType = "conversation",
                    SourceStartSeq = 1,
                    SourceEndSeq = 2,
                    SummaryText = "Active session summary",
                    GeneratedByModel = "runtime_template",
                    GeneratedAt = now.AddMinutes(-5),
                    ExpiresAt = now.AddHours(1),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now.AddMinutes(-5),
                    LastModifyTime = now.AddMinutes(-5)
                });
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new SummaryMemoryRepository(dbContext);
            var memory = await repository.GetLatestActiveAsync("tenant-1", "session-1", "run-1", CancellationToken.None);

            Assert.NotNull(memory);
            Assert.Equal("summary-active-session", memory!.Id);
        }
    }
}
