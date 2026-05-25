using BestAgent.Domain.Agents;
using BestAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.UnitTests.Domain;

public sealed class QueryFilterTests
{
    [Fact]
    public async Task AuditedEntities_ShouldFilterDeletedRowsByDefault()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BestAgentDbContext(options);
        dbContext.AgentDefinitions.AddRange(
            new AgentDefinition
            {
                Id = "a1",
                Code = "visible",
                Name = "Visible",
                DefaultModel = "gpt",
                CreateTime = DateTimeOffset.UtcNow,
                LastModifyTime = DateTimeOffset.UtcNow,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                Deleted = false
            },
            new AgentDefinition
            {
                Id = "a2",
                Code = "deleted",
                Name = "Deleted",
                DefaultModel = "gpt",
                CreateTime = DateTimeOffset.UtcNow,
                LastModifyTime = DateTimeOffset.UtcNow,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                Deleted = true
            });

        await dbContext.SaveChangesAsync();

        var count = await dbContext.AgentDefinitions.CountAsync();

        Assert.Equal(1, count);
    }
}
