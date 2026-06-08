using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using BestAgent.Api.Tests.Observability;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Knowledge;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Runtime;

public class RuntimeContextComposerTests
{
    private readonly ISummaryMemoryRepository _summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
    private readonly IKnowledgeChunkRepository _knowledgeChunkRepository = Substitute.For<IKnowledgeChunkRepository>();
    private readonly ISessionMemoryRepository _sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
    private readonly IUserMemoryRepository _userMemoryRepository = Substitute.For<IUserMemoryRepository>();
    private readonly IAgentMetrics _agentMetrics = Substitute.For<IAgentMetrics>();

    [Fact]
    public async Task ComposeModelInputAsync_ShouldReturnOriginalInput_WhenNoSummaryOrKnowledgeFound()
    {
        var composer = CreateComposer();
        var context = CreateContext();
        var definition = CreateDefinition();

        var result = await composer.ComposeModelInputAsync(context, definition, CancellationToken.None);

        Assert.Equal("hello", result.ModelInput);
        Assert.Null(result.Retrieval);
    }

    [Fact]
    public async Task ComposeModelInputAsync_ShouldAppendSummaryAndKnowledge_WhenAvailable()
    {
        var composer = CreateComposer();
        var context = CreateContext() with
        {
            Run = CreateRun() with
            {
                TenantId = "tenant-1",
                UserId = "user-1",
                SessionId = "session-1"
            }
        };
        var definition = CreateDefinition(
            knowledgeSources: """
            ["faq","sop"]
            """,
            memoryPolicy: """
            {"includeSummary":true,"maxKnowledgeChunks":2,"knowledgeCandidateCount":6,"includeSessionMemory":true,"maxSessionMemories":2,"includeUserMemory":true,"maxUserMemories":2}
            """);
        var expectedCodes = new[] { "faq", "sop" };

        _summaryMemoryRepository.GetLatestActiveAsync("tenant-1", "session-1", "run-001", Arg.Any<CancellationToken>())
            .Returns(new SummaryMemory
            {
                Id = "summary-1",
                TenantId = "tenant-1",
                RunId = "run-001",
                SessionId = "session-1",
                SummaryText = "User is trying to plan a trip."
            });
        _sessionMemoryRepository.ListActiveBySessionAsync("tenant-1", "session-1", 2, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SessionMemory
                {
                    Id = "session-memory-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    RunId = "run-001",
                    ContentJson = "{\"lastTool\":\"weather\"}"
                }
            ]);
        _userMemoryRepository.ListActiveByUserAsync("tenant-1", "user-1", 2, Arg.Any<CancellationToken>())
            .Returns(
            [
                new UserMemory
                {
                    Id = "user-memory-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    MemoryKey = "seat_preference",
                    MemoryType = "preference",
                    MemoryValue = "\"aisle\""
                }
            ]);
        _knowledgeChunkRepository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                Arg.Is<IReadOnlyList<string>>(codes => codes.SequenceEqual(expectedCodes)),
                Arg.Is<string>(query => query.Contains("hello", StringComparison.OrdinalIgnoreCase)),
                6,
                2,
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new KnowledgeChunk
                {
                    Id = "chunk-1",
                    DocumentId = "doc-1",
                    TenantId = "tenant-1",
                    ChunkNo = 1,
                    Content = "Flights can be changed within 24 hours.",
                    Source = "faq/doc-1#1"
                },
                new KnowledgeChunk
                {
                    Id = "chunk-2",
                    DocumentId = "doc-2",
                    TenantId = "tenant-1",
                    ChunkNo = 1,
                    Content = "Hotel refunds require manager approval.",
                    Source = "sop/doc-2#1"
                }
            ]);

        var result = await composer.ComposeModelInputAsync(context, definition, CancellationToken.None);

        var normalized = result.ModelInput.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("Current user input:\nhello", normalized);
        Assert.Contains("Conversation summary:\nUser is trying to plan a trip.", normalized);
        Assert.Contains("Session memory:\n- {\"lastTool\":\"weather\"}", normalized);
        Assert.Contains("User memory:\n- seat_preference: \"aisle\"", normalized);
        Assert.Contains("[1] Flights can be changed within 24 hours.", result.ModelInput);
        Assert.Contains("Citation: score=", result.ModelInput);
        Assert.Contains("Source: faq/doc-1#1", result.ModelInput);
        Assert.Contains("[2] Hotel refunds require manager approval.", result.ModelInput);
        Assert.Contains("Source: sop/doc-2#1", result.ModelInput);
        Assert.NotNull(result.Retrieval);
        Assert.Equal("hello", result.Retrieval!.QueryText);
        Assert.False(result.Retrieval.WasRewritten);
        Assert.Equal(6, result.Retrieval.CandidateCount);
        Assert.Equal(2, result.Retrieval.SelectedCount);
        Assert.Equal(["faq", "sop"], result.Retrieval.RequestedSources);
        Assert.Equal(["faq/doc-1#1", "sop/doc-2#1"], result.Retrieval.SelectedSources);
        Assert.Equal(2, result.Retrieval.Citations.Count);
    }

    [Fact]
    public async Task ComposeModelInputAsync_ShouldRewriteStructuredFollowUpInput_ForRetrievalQuery()
    {
        using var collector = new ActivityTestCollector(AgentTracing.SourceName);
        var composer = CreateComposer();
        var context = new AgentLoopContext(
            CreateRun() with
            {
                TenantId = "tenant-1",
                SessionId = "session-1",
                UserId = "user-1"
            },
            CreateDefinition().Version,
            """
            Original user input:
            Can I refund my hotel after booking?

            Tool called:
            policy_lookup

            Tool result:
            Hotel refunds require manager approval within 24 hours.

            Produce the final user-facing answer now.
            """,
            3,
            0);
        var definition = CreateDefinition(
            knowledgeSources: """
            ["faq"]
            """,
            memoryPolicy: """
            {"maxKnowledgeChunks":1,"knowledgeCandidateCount":4}
            """);
        var expectedCodes = new[] { "faq" };

        _knowledgeChunkRepository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                Arg.Is<IReadOnlyList<string>>(codes => codes.SequenceEqual(expectedCodes)),
                Arg.Is<string>(query =>
                    query.Contains("Can I refund my hotel after booking?", StringComparison.Ordinal) &&
                    query.Contains("policy_lookup", StringComparison.Ordinal) &&
                    query.Contains("Hotel refunds require manager approval within 24 hours.", StringComparison.Ordinal) &&
                    !query.Contains("Produce the final user-facing answer now.", StringComparison.Ordinal)),
                4,
                1,
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new KnowledgeChunk
                {
                    Id = "chunk-1",
                    DocumentId = "doc-1",
                    TenantId = "tenant-1",
                    ChunkNo = 1,
                    Content = "Refund approvals require a manager review.",
                    Source = "faq/doc-1#1"
                }
            ]);

        var result = await composer.ComposeModelInputAsync(context, definition, CancellationToken.None);

        Assert.Contains("Reference knowledge:", result.ModelInput);
        Assert.NotNull(result.Retrieval);
        Assert.True(result.Retrieval!.WasRewritten);
        Assert.Equal(4, result.Retrieval.CandidateCount);
        Assert.Equal(1, result.Retrieval.SelectedCount);
        Assert.Equal(["faq"], result.Retrieval.RequestedSources);
        Assert.Equal(["faq/doc-1#1"], result.Retrieval.SelectedSources);
        await _knowledgeChunkRepository.Received(1).ListByKnowledgeSourceCodesAsync(
            "tenant-1",
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(),
            4,
            1,
            Arg.Any<CancellationToken>());
        _agentMetrics.Received(1).RecordRetrieval("completed", true, 1, 4, 1, Arg.Any<TimeSpan>());
        var activity = Assert.Single(collector.Activities, value => value.OperationName == AgentTracing.RetrievalActivityName);
        Assert.Equal("run-001", activity.GetTagItem("bestagent.run_id"));
        Assert.Equal("completed", activity.GetTagItem("bestagent.retrieval_status"));
        Assert.Equal(1, activity.GetTagItem("bestagent.retrieval_selected_count"));
        Assert.Equal(true, activity.GetTagItem("bestagent.retrieval_query_rewritten"));
    }

    [Fact]
    public async Task ComposeModelInputAsync_ShouldSkipKnowledgeLookup_WhenMemoryPolicyDisablesKnowledge()
    {
        var composer = CreateComposer();
        var context = CreateContext() with
        {
            Run = CreateRun() with
            {
                TenantId = "tenant-1",
                UserId = "user-1",
                SessionId = "session-1"
            }
        };
        var definition = CreateDefinition(
            knowledgeSources: """
            ["faq"]
            """,
            memoryPolicy: """
            {"includeKnowledge":false,"includeSummary":false,"includeSessionMemory":false,"includeUserMemory":false}
            """);

        var result = await composer.ComposeModelInputAsync(context, definition, CancellationToken.None);

        Assert.Equal("hello", result.ModelInput);
        Assert.Null(result.Retrieval);
        await _knowledgeChunkRepository.DidNotReceive().ListByKnowledgeSourceCodesAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ComposeModelInputAsync_ShouldOmitCitationMetadata_WhenContextPolicyDisablesCitations()
    {
        var composer = CreateComposer();
        var context = CreateContext() with
        {
            Run = CreateRun() with
            {
                TenantId = "tenant-1",
                UserId = "user-1",
                SessionId = "session-1"
            }
        };
        var definition = CreateDefinition(
            knowledgeSources: """
            ["faq"]
            """,
            memoryPolicy: """
            {"maxKnowledgeChunks":1,"knowledgeCandidateCount":4}
            """,
            contextPolicy: """
            {"citations":false}
            """);

        _knowledgeChunkRepository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(),
                4,
                1,
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new KnowledgeChunk
                {
                    Id = "chunk-1",
                    DocumentId = "doc-1",
                    TenantId = "tenant-1",
                    ChunkNo = 1,
                    Content = "Flights can be changed within 24 hours.",
                    Source = "faq/doc-1#1"
                }
            ]);

        var result = await composer.ComposeModelInputAsync(context, definition, CancellationToken.None);

        Assert.Contains("Reference knowledge:", result.ModelInput);
        Assert.Contains("[1] Flights can be changed within 24 hours.", result.ModelInput);
        Assert.DoesNotContain("Citation:", result.ModelInput);
        Assert.DoesNotContain("Source:", result.ModelInput);
        Assert.NotNull(result.Retrieval);
        Assert.Single(result.Retrieval!.Citations);
    }

    private RuntimeContextComposer CreateComposer()
    {
        return new RuntimeContextComposer(
            _summaryMemoryRepository,
            _knowledgeChunkRepository,
            _sessionMemoryRepository,
            _userMemoryRepository,
            _agentMetrics);
    }

    private static AgentLoopContext CreateContext()
    {
        return new AgentLoopContext(CreateRun(), CreateDefinition().Version, "hello", 3, 0);
    }

    private static AgentRun CreateRun()
    {
        return new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            Status = "Running",
            InputPayload = "hello",
            CurrentStepNo = 2,
            MaxTurns = 5
        };
    }

    private static ResolvedAgentDefinition CreateDefinition(
        string? knowledgeSources = null,
        string? memoryPolicy = null,
        string? contextPolicy = null)
    {
        var now = DateTime.UtcNow;
        return new ResolvedAgentDefinition(
            new AgentDefinition
            {
                Id = "def-1",
                Code = "writer",
                Name = "Writer",
                Enabled = true,
                CurrentVersion = 1,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            },
            new AgentDefinitionVersion
            {
                Id = "ver-1",
                AgentDefinitionId = "def-1",
                Version = 1,
                Status = "Published",
                Name = "Writer v1",
                DefaultModel = "gpt-4o",
                SystemPromptTemplate = "You are a writer.",
                AllowedTools = "[\"weather\"]",
                KnowledgeSources = knowledgeSources,
                MemoryPolicy = memoryPolicy,
                ContextPolicy = contextPolicy,
                MaxTurns = 5,
                MaxCost = 10m,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            });
    }
}
