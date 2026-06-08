using BestAgent.Application.AgentDefinitions.Commands.CreateRouteRule;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentDefinitions;
using Xunit;

namespace BestAgent.Api.Tests.AgentDefinitions.Commands;

public class RouteRuleCommandHandlerTests
{
    [Fact]
    public async Task CreateRouteRule_ShouldNormalizeValues_AndPersist()
    {
        var version = CreateVersion();
        var agentDefinitionRepository = new FakeAgentDefinitionRepository
        {
            GetVersionByCodeAsyncResult = version
        };
        var routeRuleRepository = new FakeRouteRuleRepository();
        var handler = new CreateRouteRuleCommandHandler(agentDefinitionRepository, routeRuleRepository);

        var result = await handler.Handle(
            new CreateRouteRuleCommand(
                " writer ",
                2,
                " support_agent ",
                " Refund Support ",
                15,
                " intent ",
                """
                { "intent": "refund" }
                """,
                " Delegate_And_Merge ",
                " all_results ",
                """
                { "mode": "summary_only" }
                """,
                """
                { "mode": "read_only" }
                """,
                """
                { "inherit": false }
                """,
                """
                { "sources": ["faq"] }
                """,
                true,
                true),
            CancellationToken.None);

        Assert.Equal(version.Id, result.AgentDefinitionVersionId);
        Assert.Equal("writer", result.SourceAgentCode);
        Assert.Equal("support_agent", result.TargetAgentCode);
        Assert.Equal("Refund Support", result.RuleName);
        Assert.Equal(15, result.Priority);
        Assert.Equal("intent", result.MatchType);
        Assert.Equal("{\"intent\":\"refund\"}", result.MatchExpression);
        Assert.Equal("delegate_and_merge", result.HandoffMode);
        Assert.Equal("all_results", result.MergeStrategy);
        Assert.Equal("{\"mode\":\"summary_only\"}", result.ContextScope);
        Assert.Equal("{\"mode\":\"read_only\"}", result.MemoryScope);
        Assert.Equal("{\"inherit\":false}", result.ToolScope);
        Assert.Equal("{\"sources\":[\"faq\"]}", result.KnowledgeScope);
        Assert.True(result.ApprovalRequired);
        Assert.True(result.Enabled);
        Assert.NotNull(routeRuleRepository.AddedRouteRule);
        Assert.Equal("writer", routeRuleRepository.AddedRouteRule!.SourceAgentCode);
        Assert.Equal("Refund Support", routeRuleRepository.AddedRouteRule.RuleName);
        Assert.Equal("delegate_and_merge", routeRuleRepository.AddedRouteRule.HandoffMode);
        Assert.Equal("all_results", routeRuleRepository.AddedRouteRule.MergeStrategy);
    }

    [Fact]
    public async Task CreateRouteRule_ShouldThrow_WhenVersionDoesNotExist()
    {
        var handler = new CreateRouteRuleCommandHandler(
            new FakeAgentDefinitionRepository(),
            new FakeRouteRuleRepository());

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateRouteRuleCommand(
                "writer",
                2,
                "support_agent",
                "Support",
                10,
                "intent",
                null,
                "route_only",
                null,
                null,
                null,
                null,
                null,
                false,
                true),
            CancellationToken.None));

        Assert.Equal("Entity 'AgentDefinitionVersion' with key 'writer:2' was not found.", exception.Message);
    }

    [Fact]
    public async Task CreateRouteRule_ShouldThrow_WhenRuleNameAlreadyExists()
    {
        var version = CreateVersion();
        var routeRuleRepository = new FakeRouteRuleRepository
        {
            ExistsByVersionIdAndRuleNameAsyncResult = true
        };
        var handler = new CreateRouteRuleCommandHandler(
            new FakeAgentDefinitionRepository
            {
                GetVersionByCodeAsyncResult = version
            },
            routeRuleRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateRouteRuleCommand(
                "writer",
                2,
                "support_agent",
                "Support",
                10,
                "intent",
                null,
                "route_only",
                null,
                null,
                null,
                null,
                null,
                false,
                true),
            CancellationToken.None));

        Assert.Equal("Route rule 'Support' already exists for agent 'writer' version '2'.", exception.Message);
    }

    private static AgentDefinitionVersion CreateVersion()
    {
        var now = new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc);
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
        public bool ExistsByVersionIdAndRuleNameAsyncResult { get; set; }
        public RouteRule? AddedRouteRule { get; private set; }

        public Task<IReadOnlyList<RouteRule>> GetByAgentDefinitionVersionIdAsync(
            string agentDefinitionVersionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsByVersionIdAndRuleNameAsync(
            string agentDefinitionVersionId,
            string ruleName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ExistsByVersionIdAndRuleNameAsyncResult);
        }

        public Task AddAsync(RouteRule routeRule, CancellationToken cancellationToken)
        {
            AddedRouteRule = routeRule;
            return Task.CompletedTask;
        }
    }
}
