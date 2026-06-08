using BestAgent.Application.AgentDefinitions;
using BestAgent.Application.AgentDefinitions.Commands.ActivateAgentDefinitionVersion;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinition;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinitionVersion;
using BestAgent.Domain.AgentDefinitions;
using Xunit;

namespace BestAgent.Api.Tests.AgentDefinitions.Commands;

public class AgentDefinitionCommandHandlerTests
{
    [Fact]
    public async Task CreateAgentDefinition_ShouldTrimValues_CreateInitialVersion_AndPersist()
    {
        var repository = new FakeAgentDefinitionRepository
        {
            ExistsByCodeAsyncResult = false
        };
        var handler = new CreateAgentDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateAgentDefinitionCommand(
                " writer ",
                " Writer ",
                " description ",
                " instruction ",
                " system prompt ",
                " gpt-4.1 ",
                ["weather", "search"],
                ["faq", "travel-guide"],
                """
                { "includeKnowledge": true, "maxKnowledgeChunks": 5 }
                """,
                """
                { "strategy": "single-agent" }
                """,
                "{\"allowedApproverRoles\":[\"security\"]}",
                """
                { "mode": "bounded" }
                """,
                """
                { "planner": "default" }
                """,
                """
                { "citations": true }
                """,
                ["support_agent", "finance_agent"],
                """
                { "type": "object", "required": ["answer"] }
                """,
                8,
                12.5m,
                true,
                ["search"]),
            CancellationToken.None);

        Assert.Equal("writer", result.Code);
        Assert.Equal("Writer", result.Name);
        Assert.Equal("description", result.Description);
        Assert.Equal("instruction", result.Instruction);
        Assert.Equal("system prompt", result.SystemPromptTemplate);
        Assert.Equal("gpt-4.1", result.DefaultModel);
        Assert.Equal(["weather", "search"], result.AllowedTools);
        Assert.Equal(["search"], result.DeniedTools);
        Assert.Equal(["faq", "travel-guide"], result.KnowledgeSources);
        Assert.Equal("{\"includeKnowledge\":true,\"maxKnowledgeChunks\":5}", result.MemoryPolicy);
        Assert.Equal("{\"strategy\":\"single-agent\"}", result.RoutingPolicy);
        Assert.Equal(
            "{\"ApprovalRequiredSideEffectLevels\":[],\"RoleRequiredSideEffectLevels\":[],\"AllowedApproverRoles\":[\"security\"],\"ParameterApprovalRules\":[]}",
            result.ApprovalPolicy);
        Assert.Equal("{\"mode\":\"bounded\"}", result.ExecutionPolicy);
        Assert.Equal("{\"planner\":\"default\"}", result.PlannerPolicy);
        Assert.Equal("{\"citations\":true}", result.ContextPolicy);
        Assert.Equal(["support_agent", "finance_agent"], result.AllowedHandoffs);
        Assert.Equal("{\"type\":\"object\",\"required\":[\"answer\"]}", result.OutputSchema);
        Assert.Equal(1, result.CurrentVersion);
        Assert.Equal(1, result.Version);
        Assert.Equal(AgentDefinitionVersionStatuses.Published, result.VersionStatus);
        Assert.NotNull(repository.AddedDefinition);
        Assert.Equal("writer", repository.AddedDefinition!.Definition.Code);
        Assert.Equal("Writer", repository.AddedDefinition.Definition.Name);
        Assert.Equal("description", repository.AddedDefinition.Definition.Description);
        Assert.Equal("instruction", repository.AddedDefinition.Version.Instruction);
        Assert.Equal("system prompt", repository.AddedDefinition.Version.SystemPromptTemplate);
        Assert.Equal("gpt-4.1", repository.AddedDefinition.Version.DefaultModel);
        Assert.Equal("[\"weather\",\"search\"]", repository.AddedDefinition.Version.AllowedTools);
        Assert.Equal("[\"search\"]", repository.AddedDefinition.Version.DeniedTools);
        Assert.Equal("[\"faq\",\"travel-guide\"]", repository.AddedDefinition.Version.KnowledgeSources);
        Assert.Equal("{\"includeKnowledge\":true,\"maxKnowledgeChunks\":5}", repository.AddedDefinition.Version.MemoryPolicy);
        Assert.Equal("{\"strategy\":\"single-agent\"}", repository.AddedDefinition.Version.RoutingPolicy);
        Assert.Equal(
            "{\"ApprovalRequiredSideEffectLevels\":[],\"RoleRequiredSideEffectLevels\":[],\"AllowedApproverRoles\":[\"security\"],\"ParameterApprovalRules\":[]}",
            repository.AddedDefinition.Version.ApprovalPolicy);
        Assert.Equal("{\"mode\":\"bounded\"}", repository.AddedDefinition.Version.ExecutionPolicy);
        Assert.Equal("{\"planner\":\"default\"}", repository.AddedDefinition.Version.PlannerPolicy);
        Assert.Equal("{\"citations\":true}", repository.AddedDefinition.Version.ContextPolicy);
        Assert.Equal("[\"support_agent\",\"finance_agent\"]", repository.AddedDefinition.Version.AllowedHandoffs);
        Assert.Equal("{\"type\":\"object\",\"required\":[\"answer\"]}", repository.AddedDefinition.Version.OutputSchema);
        Assert.Equal(AgentDefinitionVersionStatuses.Published, repository.AddedDefinition.Version.Status);
        Assert.NotNull(repository.AddedDefinition.Version.PublishedAt);
    }

    [Fact]
    public async Task CreateAgentDefinition_ShouldThrow_WhenCodeAlreadyExists()
    {
        var repository = new FakeAgentDefinitionRepository
        {
            ExistsByCodeAsyncResult = true
        };
        var handler = new CreateAgentDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateAgentDefinitionCommand("writer", "Writer", null, null, "system prompt", "gpt-4.1", null, null, null, null, null, null, null, null, null, null, 8, 12.5m, true),
            CancellationToken.None));

        Assert.Equal("Agent definition code 'writer' already exists.", exception.Message);
    }

    [Fact]
    public async Task CreateAgentDefinitionVersion_ShouldCreateNextDraftVersion_AndUseFallbackName()
    {
        var definition = CreateResolvedDefinition(currentVersion: 2);
        var repository = new FakeAgentDefinitionRepository
        {
            GetByCodeAsyncResult = definition,
            GetVersionsAsyncResult =
            [
                definition.Version with { Version = 2, Name = "Writer v2" },
                definition.Version with { Id = "version-001", Version = 1, Status = AgentDefinitionVersionStatuses.Archived, Name = "Writer v1" }
            ]
        };
        var handler = new CreateAgentDefinitionVersionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateAgentDefinitionVersionCommand(
                "writer",
                "  ",
                " improved draft ",
                " updated instruction ",
                " system prompt v3 ",
                " gpt-4.1-mini ",
                ["weather"],
                ["faq"],
                """
                { "includeSummary": false, "includeUserMemory": true }
                """,
                """
                { "strategy": "handoff-first" }
                """,
                "{\"allowedApproverRoles\":[\"security\"],\"approvalRequiredSideEffectLevels\":[\"destructive\"]}",
                """
                { "mode": "strict" }
                """,
                """
                { "planner": "multi-step" }
                """,
                """
                { "contextWindow": "extended" }
                """,
                ["refund_agent"],
                """
                { "type": "string", "minLength": 3 }
                """,
                10,
                20m,
                ["weather"]),
            CancellationToken.None);

        Assert.Equal(3, result.Version);
        Assert.Equal("Writer v3", result.Name);
        Assert.Equal("improved draft", result.Description);
        Assert.Equal("updated instruction", result.Instruction);
        Assert.Equal("system prompt v3", result.SystemPromptTemplate);
        Assert.Equal("gpt-4.1-mini", result.DefaultModel);
        Assert.Equal(["weather"], result.AllowedTools);
        Assert.Equal(["weather"], result.DeniedTools);
        Assert.Equal(["faq"], result.KnowledgeSources);
        Assert.Equal("{\"includeSummary\":false,\"includeUserMemory\":true}", result.MemoryPolicy);
        Assert.Equal("{\"strategy\":\"handoff-first\"}", result.RoutingPolicy);
        Assert.Equal("{\"mode\":\"strict\"}", result.ExecutionPolicy);
        Assert.Equal("{\"planner\":\"multi-step\"}", result.PlannerPolicy);
        Assert.Equal("{\"contextWindow\":\"extended\"}", result.ContextPolicy);
        Assert.Equal(["refund_agent"], result.AllowedHandoffs);
        Assert.Equal("{\"type\":\"string\",\"minLength\":3}", result.OutputSchema);
        Assert.False(result.IsCurrentVersion);
        Assert.NotNull(repository.AddedVersion);
        Assert.Equal(3, repository.AddedVersion!.Version);
        Assert.Equal(AgentDefinitionVersionStatuses.Draft, repository.AddedVersion.Status);
        Assert.Equal("Writer v3", repository.AddedVersion.Name);
        Assert.Equal("improved draft", repository.AddedVersion.Description);
        Assert.Equal("updated instruction", repository.AddedVersion.Instruction);
        Assert.Equal("[\"weather\"]", repository.AddedVersion.AllowedTools);
        Assert.Equal("[\"weather\"]", repository.AddedVersion.DeniedTools);
        Assert.Equal("[\"faq\"]", repository.AddedVersion.KnowledgeSources);
        Assert.Equal("{\"includeSummary\":false,\"includeUserMemory\":true}", repository.AddedVersion.MemoryPolicy);
        Assert.Equal("{\"strategy\":\"handoff-first\"}", repository.AddedVersion.RoutingPolicy);
        Assert.Equal(
            "{\"ApprovalRequiredSideEffectLevels\":[\"destructive\"],\"RoleRequiredSideEffectLevels\":[],\"AllowedApproverRoles\":[\"security\"],\"ParameterApprovalRules\":[]}",
            repository.AddedVersion.ApprovalPolicy);
        Assert.Equal("{\"mode\":\"strict\"}", repository.AddedVersion.ExecutionPolicy);
        Assert.Equal("{\"planner\":\"multi-step\"}", repository.AddedVersion.PlannerPolicy);
        Assert.Equal("{\"contextWindow\":\"extended\"}", repository.AddedVersion.ContextPolicy);
        Assert.Equal("[\"refund_agent\"]", repository.AddedVersion.AllowedHandoffs);
        Assert.Equal("{\"type\":\"string\",\"minLength\":3}", repository.AddedVersion.OutputSchema);
    }

    [Fact]
    public async Task ActivateAgentDefinitionVersion_ShouldArchiveCurrentVersion_AndPublishTargetVersion()
    {
        var current = CreateResolvedDefinition(currentVersion: 1, version: 1, versionStatus: AgentDefinitionVersionStatuses.Published, versionName: "Writer v1");
        var targetVersion = current.Version with
        {
            Id = "version-002",
            Version = 2,
            Status = AgentDefinitionVersionStatuses.Draft,
            Name = "Writer v2",
            PublishedAt = null
        };
        var repository = new FakeAgentDefinitionRepository
        {
            GetByCodeAsyncResult = current,
            GetVersionByCodeAsyncResult = targetVersion
        };
        var handler = new ActivateAgentDefinitionVersionCommandHandler(repository);

        var result = await handler.Handle(
            new ActivateAgentDefinitionVersionCommand("writer", 2),
            CancellationToken.None);

        Assert.Equal("writer", result.Code);
        Assert.Equal(2, result.CurrentVersion);
        Assert.Equal(2, result.Version);
        Assert.Equal(AgentDefinitionVersionStatuses.Published, result.VersionStatus);
        Assert.NotNull(repository.ActivatedDefinition);
        Assert.NotNull(repository.ActivatedTargetVersion);
        Assert.NotNull(repository.ActivatedPreviousVersion);
        Assert.Equal(2, repository.ActivatedDefinition!.CurrentVersion);
        Assert.Equal(AgentDefinitionVersionStatuses.Published, repository.ActivatedTargetVersion!.Status);
        Assert.NotNull(repository.ActivatedTargetVersion.PublishedAt);
        Assert.Equal(AgentDefinitionVersionStatuses.Archived, repository.ActivatedPreviousVersion!.Status);
        Assert.Equal(1, repository.ActivatedPreviousVersion.Version);
    }

    private static ResolvedAgentDefinition CreateResolvedDefinition(
        int currentVersion = 1,
        int version = 1,
        string versionStatus = AgentDefinitionVersionStatuses.Published,
        string versionName = "Writer v1")
    {
        var now = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
        var definitionId = "definition-001";
        return new ResolvedAgentDefinition(
            new AgentDefinition
            {
                Id = definitionId,
                Code = "writer",
                Name = "Writer",
                Description = "description",
                Enabled = true,
                CurrentVersion = currentVersion,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            },
            new AgentDefinitionVersion
            {
                Id = "version-current",
                AgentDefinitionId = definitionId,
                Version = version,
                Status = versionStatus,
                Name = versionName,
                Description = "version description",
                Instruction = "instruction",
                SystemPromptTemplate = "system prompt",
                DefaultModel = "gpt-4.1",
                AllowedTools = "[\"weather\",\"search\"]",
                DeniedTools = "[\"search\"]",
                KnowledgeSources = "[\"faq\",\"travel-guide\"]",
                MemoryPolicy = "{\"includeKnowledge\":true}",
                RoutingPolicy = "{\"strategy\":\"single-agent\"}",
                ApprovalPolicy = "{\"AllowedApproverRoles\":[\"ops\"]}",
                ExecutionPolicy = "{\"mode\":\"bounded\"}",
                PlannerPolicy = "{\"planner\":\"default\"}",
                ContextPolicy = "{\"citations\":true}",
                AllowedHandoffs = "[\"support_agent\",\"finance_agent\"]",
                OutputSchema = "{\"type\":\"object\",\"required\":[\"answer\"]}",
                MaxTurns = 8,
                MaxCost = 12.5m,
                PublishedAt = versionStatus == AgentDefinitionVersionStatuses.Published ? now : null,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            });
    }

    private sealed class FakeAgentDefinitionRepository : IAgentDefinitionRepository
    {
        public bool ExistsByCodeAsyncResult { get; set; }
        public ResolvedAgentDefinition? GetByCodeAsyncResult { get; set; }
        public IReadOnlyList<AgentDefinitionVersion> GetVersionsAsyncResult { get; set; } = [];
        public AgentDefinitionVersion? GetVersionByCodeAsyncResult { get; set; }
        public ResolvedAgentDefinition? AddedDefinition { get; private set; }
        public AgentDefinitionVersion? AddedVersion { get; private set; }
        public AgentDefinition? ActivatedDefinition { get; private set; }
        public AgentDefinitionVersion? ActivatedTargetVersion { get; private set; }
        public AgentDefinitionVersion? ActivatedPreviousVersion { get; private set; }

        public Task<ResolvedAgentDefinition?> GetEnabledByCodeAsync(string agentCode, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ResolvedAgentDefinition?> GetByCodeAsync(string agentCode, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetByCodeAsyncResult);
        }

        public Task<ResolvedAgentDefinition?> GetByVersionIdAsync(string versionId, CancellationToken cancellationToken)
        {
            if (GetByCodeAsyncResult is not null
                && string.Equals(GetByCodeAsyncResult.Version.Id, versionId, StringComparison.Ordinal))
            {
                return Task.FromResult<ResolvedAgentDefinition?>(GetByCodeAsyncResult);
            }

            return Task.FromResult<ResolvedAgentDefinition?>(null);
        }

        public Task<IReadOnlyList<ResolvedAgentDefinition>> GetAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AgentDefinitionVersion>> GetVersionsAsync(string agentCode, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetVersionsAsyncResult);
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
            return Task.FromResult(ExistsByCodeAsyncResult);
        }

        public Task AddAsync(ResolvedAgentDefinition definition, CancellationToken cancellationToken)
        {
            AddedDefinition = definition;
            return Task.CompletedTask;
        }

        public Task AddVersionAsync(AgentDefinitionVersion version, CancellationToken cancellationToken)
        {
            AddedVersion = version;
            return Task.CompletedTask;
        }

        public Task ActivateVersionAsync(
            AgentDefinition definition,
            AgentDefinitionVersion targetVersion,
            AgentDefinitionVersion? previousVersion,
            CancellationToken cancellationToken)
        {
            ActivatedDefinition = definition;
            ActivatedTargetVersion = targetVersion;
            ActivatedPreviousVersion = previousVersion;
            return Task.CompletedTask;
        }
    }
}
