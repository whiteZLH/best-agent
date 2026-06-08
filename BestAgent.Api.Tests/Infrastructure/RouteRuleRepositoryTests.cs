using BestAgent.Domain.AgentDefinitions;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class RouteRuleRepositoryTests
{
    [Fact]
    public async Task ShouldAddListAndCheckExistence()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new RouteRuleRepository(dbContext);

            await repository.AddAsync(
                CreateRouteRule("rule-2", "version-1", "writer", "finance_agent", "Finance", 20, now.AddMinutes(1)),
                CancellationToken.None);
            await repository.AddAsync(
                CreateRouteRule("rule-1", "version-1", "writer", "support_agent", "Support", 10, now),
                CancellationToken.None);
            await repository.AddAsync(
                CreateRouteRule("rule-3", "version-2", "planner", "ops_agent", "Ops", 5, now.AddMinutes(2)),
                CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new RouteRuleRepository(dbContext);

            var rules = await repository.GetByAgentDefinitionVersionIdAsync("version-1", CancellationToken.None);
            var exists = await repository.ExistsByVersionIdAndRuleNameAsync("version-1", "Support", CancellationToken.None);
            var missing = await repository.ExistsByVersionIdAndRuleNameAsync("version-1", "Missing", CancellationToken.None);

            Assert.Equal(["rule-1", "rule-2"], rules.Select(x => x.Id));
            Assert.True(exists);
            Assert.False(missing);
        }
    }

    [Fact]
    public async Task Queries_ShouldIgnoreDeletedRouteRules()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 8, 11, 0, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            await dbContext.RouteRules.AddAsync(
                CreateRouteRule("rule-1", "version-1", "writer", "support_agent", "Support", 10, now) with
                {
                    Deleted = true
                });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new RouteRuleRepository(dbContext);

            var rules = await repository.GetByAgentDefinitionVersionIdAsync("version-1", CancellationToken.None);
            var exists = await repository.ExistsByVersionIdAndRuleNameAsync("version-1", "Support", CancellationToken.None);

            Assert.Empty(rules);
            Assert.False(exists);
        }
    }

    private static RouteRule CreateRouteRule(
        string id,
        string versionId,
        string sourceAgentCode,
        string targetAgentCode,
        string ruleName,
        int priority,
        DateTime now)
    {
        return new RouteRule
        {
            Id = id,
            AgentDefinitionVersionId = versionId,
            SourceAgentCode = sourceAgentCode,
            TargetAgentCode = targetAgentCode,
            RuleName = ruleName,
            Priority = priority,
            MatchType = "intent",
            MatchExpression = "{\"intent\":\"support\"}",
            HandoffMode = "route_only",
            ContextScope = "{\"mode\":\"summary_only\"}",
            MemoryScope = "{\"mode\":\"read_only\"}",
            ToolScope = "{\"inherit\":false}",
            KnowledgeScope = "{\"sources\":[\"faq\"]}",
            ApprovalRequired = false,
            Enabled = true,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }
}
