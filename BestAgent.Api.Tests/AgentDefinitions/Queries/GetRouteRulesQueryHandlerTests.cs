using BestAgent.Application.AgentDefinitions.Queries.GetRouteRules;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentDefinitions;
using Xunit;

namespace BestAgent.Api.Tests.AgentDefinitions.Queries;

public class GetRouteRulesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnMappedRouteRules()
    {
        var handler = new GetRouteRulesQueryHandler(
            new FakeAgentDefinitionRepository
            {
                GetVersionByCodeAsyncResult = CreateVersion()
            },
            new FakeRouteRuleRepository
            {
                GetByAgentDefinitionVersionIdAsyncResult =
                [
                    CreateRouteRule("rule-2", "Finance", 20),
                    CreateRouteRule("rule-1", "Support", 10)
                ]
            });

        var result = await handler.Handle(new GetRouteRulesQuery("writer", 2), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Finance", result[0].RuleName);
        Assert.Equal("supervisor_summary", result[0].MergeStrategy);
        Assert.Equal("Support", result[1].RuleName);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenVersionDoesNotExist()
    {
        var handler = new GetRouteRulesQueryHandler(
            new FakeAgentDefinitionRepository(),
            new FakeRouteRuleRepository());

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new GetRouteRulesQuery("writer", 2),
            CancellationToken.None));

        Assert.Equal("Entity 'AgentDefinitionVersion' with key 'writer:2' was not found.", exception.Message);
    }

    private static AgentDefinitionVersion CreateVersion()
    {
        var now = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        return new AgentDefinitionVersion
        {
            Id = "version-002",
            AgentDefinitionId = "definition-001",
            Version = 2,
            Status = AgentDefinitionVersionStatuses.Published,
            Name = "Writer v2",
            DefaultModel = "gpt-4.1",
            MaxTurns = 8,
            MaxCost = 12.5m,
            PublishedAt = now,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }

    private static RouteRule CreateRouteRule(string id, string ruleName, int priority)
    {
        var now = new DateTime(2026, 6, 8, 12, 30, 0, DateTimeKind.Utc);
        return new RouteRule
        {
            Id = id,
            AgentDefinitionVersionId = "version-002",
            SourceAgentCode = "writer",
            TargetAgentCode = "support_agent",
            RuleName = ruleName,
            Priority = priority,
            MatchType = "intent",
            MatchExpression = "{\"intent\":\"support\"}",
            HandoffMode = "delegate_and_merge",
            MergeStrategy = "supervisor_summary",
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

    private sealed class FakeAgentDefinitionRepository : IAgentDefinitionRepository
    {
        public AgentDefinitionVersion? GetVersionByCodeAsyncResult { get; set; }

        public Task<ResolvedAgentDefinition?> GetEnabledByCodeAsync(string agentCode, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ResolvedAgentDefinition?> GetByCodeAsync(string agentCode, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ResolvedAgentDefinition?> GetByVersionIdAsync(string versionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ResolvedAgentDefinition>> GetAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AgentDefinitionVersion>> GetVersionsAsync(string agentCode, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AgentDefinitionVersion?> GetVersionByCodeAsync(string agentCode, int version, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetVersionByCodeAsyncResult);
        }

        public Task<bool> AnyAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsByCodeAsync(string agentCode, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AddAsync(ResolvedAgentDefinition definition, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AddVersionAsync(AgentDefinitionVersion version, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ActivateVersionAsync(
            AgentDefinition definition,
            AgentDefinitionVersion targetVersion,
            AgentDefinitionVersion? previousVersion,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeRouteRuleRepository : IRouteRuleRepository
    {
        public IReadOnlyList<RouteRule> GetByAgentDefinitionVersionIdAsyncResult { get; set; } = [];

        public Task<IReadOnlyList<RouteRule>> GetByAgentDefinitionVersionIdAsync(
            string agentDefinitionVersionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetByAgentDefinitionVersionIdAsyncResult);
        }

        public Task<bool> ExistsByVersionIdAndRuleNameAsync(
            string agentDefinitionVersionId,
            string ruleName,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AddAsync(RouteRule routeRule, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
