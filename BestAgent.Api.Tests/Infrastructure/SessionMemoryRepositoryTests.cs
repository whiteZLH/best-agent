using BestAgent.Domain.Knowledge;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class SessionMemoryRepositoryTests
{
    [Fact]
    public async Task ListActiveBySessionAsync_ShouldExcludeExpiredMemories()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = DateTime.UtcNow;

        await using (var dbContext = new BestAgentDbContext(options))
        {
            dbContext.SessionMemories.AddRange(
                new SessionMemory
                {
                    Id = "memory-active",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    RunId = "run-1",
                    MemoryType = "tool_result",
                    ContentJson = "{\"tool\":\"weather\"}",
                    SourceType = "tool_result",
                    SourceRef = "run-1:weather",
                    Confidence = 1.0m,
                    ExpiresAt = now.AddMinutes(30),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                },
                new SessionMemory
                {
                    Id = "memory-expired",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    RunId = "run-2",
                    MemoryType = "tool_result",
                    ContentJson = "{\"tool\":\"profile\"}",
                    SourceType = "tool_result",
                    SourceRef = "run-2:profile",
                    Confidence = 1.0m,
                    ExpiresAt = now.AddMinutes(-1),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now.AddMinutes(-10),
                    LastModifyTime = now.AddMinutes(-10)
                });
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new SessionMemoryRepository(dbContext);
            var listed = await repository.ListActiveBySessionAsync("tenant-1", "session-1", 10, CancellationToken.None);

            var memory = Assert.Single(listed);
            Assert.Equal("memory-active", memory.Id);
        }
    }
}
