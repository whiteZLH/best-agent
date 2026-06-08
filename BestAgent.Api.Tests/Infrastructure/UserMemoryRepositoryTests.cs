using BestAgent.Domain.Knowledge;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class UserMemoryRepositoryTests
{
    [Fact]
    public async Task ShouldAddGetUpdateAndListActiveUserMemories()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new UserMemoryRepository(dbContext);
            await repository.AddAsync(
                new UserMemory
                {
                    Id = "memory-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    MemoryKey = "seat_preference",
                    MemoryScope = "user",
                    MemoryType = "preference",
                    MemoryValue = "\"aisle\"",
                    SourceType = "tool_memory",
                    SourceRef = "run-1:profile",
                    Confidence = 0.9m,
                    EffectiveAt = now,
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                },
                CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new UserMemoryRepository(dbContext);
            var fetched = await repository.GetByMemoryKeyAsync("tenant-1", "user-1", "seat_preference", CancellationToken.None);

            Assert.NotNull(fetched);
            Assert.Equal("\"aisle\"", fetched!.MemoryValue);

            await repository.UpdateAsync(
                fetched with
                {
                    MemoryValue = "\"window\"",
                    Confidence = 1.0m,
                    LastModifyTime = now.AddMinutes(5)
                },
                CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new UserMemoryRepository(dbContext);
            var fetched = await repository.GetByMemoryKeyAsync("tenant-1", "user-1", "seat_preference", CancellationToken.None);
            var listed = await repository.ListActiveByUserAsync("tenant-1", "user-1", 5, CancellationToken.None);

            Assert.NotNull(fetched);
            Assert.Equal("\"window\"", fetched!.MemoryValue);
            Assert.Single(listed);
            Assert.Equal("seat_preference", listed[0].MemoryKey);
        }
    }

    [Fact]
    public async Task ListActiveByUserAsync_ShouldExcludeExpiredAndFutureEffectiveMemories()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = DateTime.UtcNow;

        await using (var dbContext = new BestAgentDbContext(options))
        {
            dbContext.UserMemories.AddRange(
                new UserMemory
                {
                    Id = "memory-active",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    MemoryKey = "seat_preference",
                    MemoryScope = "user",
                    MemoryType = "preference",
                    MemoryValue = "\"aisle\"",
                    SourceType = "tool_memory",
                    SourceRef = "run-1:profile",
                    Confidence = 1.0m,
                    EffectiveAt = now.AddMinutes(-5),
                    ExpiresAt = now.AddHours(1),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now.AddMinutes(-5),
                    LastModifyTime = now.AddMinutes(-5)
                },
                new UserMemory
                {
                    Id = "memory-expired",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    MemoryKey = "meal_preference",
                    MemoryScope = "user",
                    MemoryType = "preference",
                    MemoryValue = "\"vegan\"",
                    SourceType = "tool_memory",
                    SourceRef = "run-1:profile",
                    Confidence = 1.0m,
                    EffectiveAt = now.AddHours(-2),
                    ExpiresAt = now.AddMinutes(-1),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now.AddHours(-2),
                    LastModifyTime = now.AddHours(-2)
                },
                new UserMemory
                {
                    Id = "memory-future",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    MemoryKey = "vip_status",
                    MemoryScope = "user",
                    MemoryType = "fact",
                    MemoryValue = "\"gold\"",
                    SourceType = "tool_memory",
                    SourceRef = "run-1:profile",
                    Confidence = 1.0m,
                    EffectiveAt = now.AddMinutes(30),
                    ExpiresAt = now.AddHours(2),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                });
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new UserMemoryRepository(dbContext);
            var listed = await repository.ListActiveByUserAsync("tenant-1", "user-1", 10, CancellationToken.None);

            var memory = Assert.Single(listed);
            Assert.Equal("seat_preference", memory.MemoryKey);
            Assert.Equal("\"aisle\"", memory.MemoryValue);
        }
    }
}
