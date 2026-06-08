using System.Text;
using System.Text.Json;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.Knowledge;
using BestAgent.Application.Observability;
using System.Diagnostics;

namespace BestAgent.Application.AgentRuns.Runtime;

public class RuntimeContextComposer : IRuntimeContextComposer
{
    private readonly ISummaryMemoryRepository _summaryMemoryRepository;
    private readonly IKnowledgeChunkRepository _knowledgeChunkRepository;
    private readonly ISessionMemoryRepository _sessionMemoryRepository;
    private readonly IUserMemoryRepository _userMemoryRepository;
    private readonly IAgentMetrics _agentMetrics;

    public RuntimeContextComposer(
        ISummaryMemoryRepository summaryMemoryRepository,
        IKnowledgeChunkRepository knowledgeChunkRepository,
        ISessionMemoryRepository sessionMemoryRepository,
        IUserMemoryRepository userMemoryRepository,
        IAgentMetrics agentMetrics)
    {
        _summaryMemoryRepository = summaryMemoryRepository;
        _knowledgeChunkRepository = knowledgeChunkRepository;
        _sessionMemoryRepository = sessionMemoryRepository;
        _userMemoryRepository = userMemoryRepository;
        _agentMetrics = agentMetrics;
    }

    public async Task<string> ComposeModelInputAsync(
        AgentLoopContext context,
        ResolvedAgentDefinition resolvedDefinition,
        CancellationToken cancellationToken)
    {
        var currentInput = context.CurrentInput;
        var policy = MemoryPolicy.Parse(resolvedDefinition.Version.MemoryPolicy);
        var includeCitations = ShouldIncludeKnowledgeCitations(resolvedDefinition.Version.ContextPolicy);
        var knowledgeSources = KnowledgeSourceListParser.Parse(resolvedDefinition.Version.KnowledgeSources);
        var retrievalQuery = RetrievalQueryBuilder.Build(currentInput, policy.KnowledgeCandidateCount);
        var sessionMemories = await LoadSessionMemoriesAsync(context, policy, cancellationToken);
        var userMemories = await LoadUserMemoriesAsync(context, policy, cancellationToken);

        SummaryMemory? summaryMemory = null;
        if (policy.IncludeSummary)
        {
            summaryMemory = await _summaryMemoryRepository.GetLatestActiveAsync(
                context.Run.TenantId,
                context.Run.SessionId,
                context.Run.RunId,
                cancellationToken);
        }

        IReadOnlyList<KnowledgeChunk> chunks = Array.Empty<KnowledgeChunk>();
        if (policy.IncludeKnowledge
            && knowledgeSources.Count > 0
            && policy.MaxKnowledgeChunks > 0)
        {
            var startedAt = DateTime.UtcNow;
            using var activity = AgentTracing.Source.StartActivity(AgentTracing.RetrievalActivityName, ActivityKind.Internal);
            activity?.SetTag("bestagent.run_id", context.Run.RunId);
            activity?.SetTag("bestagent.agent_code", context.Run.AgentCode);
            activity?.SetTag("bestagent.tenant_id", context.Run.TenantId);
            activity?.SetTag("bestagent.retrieval_query", retrievalQuery.QueryText);
            activity?.SetTag("bestagent.retrieval_query_rewritten", retrievalQuery.WasRewritten);
            activity?.SetTag("bestagent.retrieval_source_count", knowledgeSources.Count);
            activity?.SetTag("bestagent.retrieval_candidate_count", retrievalQuery.CandidateCount);
            activity?.SetTag("bestagent.retrieval_limit", policy.MaxKnowledgeChunks);

            try
            {
                chunks = await _knowledgeChunkRepository.ListByKnowledgeSourceCodesAsync(
                    context.Run.TenantId,
                    knowledgeSources,
                    retrievalQuery.QueryText,
                    retrievalQuery.CandidateCount,
                    policy.MaxKnowledgeChunks,
                    cancellationToken);

                activity?.SetTag("bestagent.retrieval_status", "completed");
                activity?.SetTag("bestagent.retrieval_selected_count", chunks.Count);
                _agentMetrics.RecordRetrieval(
                    "completed",
                    retrievalQuery.WasRewritten,
                    knowledgeSources.Count,
                    retrievalQuery.CandidateCount,
                    chunks.Count,
                    DateTime.UtcNow - startedAt);
            }
            catch
            {
                activity?.SetTag("bestagent.retrieval_status", "failed");
                _agentMetrics.RecordRetrieval(
                    "failed",
                    retrievalQuery.WasRewritten,
                    knowledgeSources.Count,
                    retrievalQuery.CandidateCount,
                    0,
                    DateTime.UtcNow - startedAt);
                throw;
            }
        }

        if (summaryMemory is null && chunks.Count == 0 && sessionMemories.Count == 0 && userMemories.Count == 0)
        {
            return currentInput;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Current user input:");
        builder.AppendLine(currentInput);

        if (summaryMemory is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Conversation summary:");
            builder.AppendLine(summaryMemory.SummaryText);
        }

        if (sessionMemories.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Session memory:");
            foreach (var memory in sessionMemories)
            {
                if (!string.IsNullOrWhiteSpace(memory.ContentJson))
                {
                    builder.AppendLine($"- {memory.ContentJson}");
                }
            }
        }

        if (userMemories.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("User memory:");
            foreach (var memory in userMemories)
            {
                var label = string.IsNullOrWhiteSpace(memory.MemoryKey) ? memory.MemoryType : memory.MemoryKey;
                builder.AppendLine($"- {label}: {memory.MemoryValue}");
            }
        }

        if (chunks.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Reference knowledge:");
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                builder.AppendLine($"[{i + 1}] {chunk.Content}");
                if (includeCitations)
                {
                    var citation = CitationFormatter.Format(chunk, retrievalQuery.QueryText);
                    builder.AppendLine($"Citation: {citation}");
                    if (!string.IsNullOrWhiteSpace(chunk.Source))
                    {
                        builder.AppendLine($"Source: {chunk.Source}");
                    }
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<IReadOnlyList<SessionMemory>> LoadSessionMemoriesAsync(
        AgentLoopContext context,
        MemoryPolicy policy,
        CancellationToken cancellationToken)
    {
        if (!policy.IncludeSessionMemory || policy.MaxSessionMemories <= 0)
        {
            return Array.Empty<SessionMemory>();
        }

        return await _sessionMemoryRepository.ListActiveBySessionAsync(
            context.Run.TenantId,
            context.Run.SessionId,
            policy.MaxSessionMemories,
            cancellationToken);
    }

    private async Task<IReadOnlyList<UserMemory>> LoadUserMemoriesAsync(
        AgentLoopContext context,
        MemoryPolicy policy,
        CancellationToken cancellationToken)
    {
        if (!policy.IncludeUserMemory || policy.MaxUserMemories <= 0)
        {
            return Array.Empty<UserMemory>();
        }

        return await _userMemoryRepository.ListActiveByUserAsync(
            context.Run.TenantId,
            context.Run.UserId,
            policy.MaxUserMemories,
            cancellationToken);
    }

    private static bool ShouldIncludeKnowledgeCitations(string? contextPolicy)
    {
        if (string.IsNullOrWhiteSpace(contextPolicy))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(contextPolicy);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("citations", out var citationsElement))
            {
                return true;
            }

            return citationsElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(citationsElement.GetString(), out var value) => value,
                _ => true
            };
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private sealed record RetrievalQuery(string QueryText, int CandidateCount, bool WasRewritten);

    private static class RetrievalQueryBuilder
    {
        private static readonly Dictionary<string, string> SectionHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Current user input:"] = "currentInput",
            ["Original user input:"] = "originalInput",
            ["Tool called:"] = "toolName",
            ["Tool result:"] = "toolResult"
        };

        public static RetrievalQuery Build(string currentInput, int candidateCount)
        {
            var normalizedCandidateCount = Math.Max(0, candidateCount);
            if (string.IsNullOrWhiteSpace(currentInput))
            {
                return new RetrievalQuery(string.Empty, normalizedCandidateCount, false);
            }

            var normalized = currentInput
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Trim();
            var rewritten = TryRewriteStructuredFollowUp(normalized);
            if (!string.IsNullOrWhiteSpace(rewritten))
            {
                return new RetrievalQuery(rewritten, normalizedCandidateCount, true);
            }

            var condensed = NormalizeFreeformQuery(normalized);
            return new RetrievalQuery(condensed, normalizedCandidateCount, false);
        }

        private static string? TryRewriteStructuredFollowUp(string input)
        {
            var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string? currentSection = null;
            var foundStructuredHeader = false;

            foreach (var rawLine in input.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (SectionHeaders.TryGetValue(line, out var nextSection))
                {
                    foundStructuredHeader = true;
                    currentSection = nextSection;
                    if (!sections.ContainsKey(currentSection))
                    {
                        sections[currentSection] = [];
                    }

                    continue;
                }

                if (string.Equals(line, "Produce the final user-facing answer now.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (currentSection is null)
                {
                    continue;
                }

                sections[currentSection].Add(line);
            }

            if (!foundStructuredHeader)
            {
                return null;
            }

            var parts = new[]
            {
                ReadSection(sections, "currentInput", 360),
                ReadSection(sections, "originalInput", 360),
                ReadSection(sections, "toolName", 80),
                ReadSection(sections, "toolResult", 420)
            };

            return JoinDistinctParts(parts);
        }

        private static string NormalizeFreeformQuery(string input)
        {
            var lines = input
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(12);
            var condensed = string.Join(' ', lines).Trim();
            return condensed.Length <= 600
                ? condensed
                : condensed[..600].TrimEnd();
        }

        private static string? ReadSection(
            IReadOnlyDictionary<string, List<string>> sections,
            string sectionName,
            int maxLength)
        {
            if (!sections.TryGetValue(sectionName, out var lines) || lines.Count == 0)
            {
                return null;
            }

            var value = string.Join(' ', lines).Trim();
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value[..maxLength].TrimEnd();
        }

        private static string JoinDistinctParts(IEnumerable<string?> parts)
        {
            var normalizedParts = parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return string.Join(' ', normalizedParts);
        }
    }

    private static class CitationFormatter
    {
        public static string Format(KnowledgeChunk chunk, string queryText)
        {
            var score = Score(chunk, queryText);
            var source = string.IsNullOrWhiteSpace(chunk.Source)
                ? chunk.DocumentId
                : chunk.Source;
            return $"score={score}; source={source}; chunk={chunk.ChunkNo}";
        }

        private static int Score(KnowledgeChunk chunk, string queryText)
        {
            if (string.IsNullOrWhiteSpace(queryText))
            {
                return 0;
            }

            var haystack = $"{chunk.Content} {chunk.Source} {chunk.Metadata}".ToLowerInvariant();
            var terms = queryText
                .ToLowerInvariant()
                .Split(
                    [' ', '\r', '\n', '\t', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => term.Length >= 2)
                .Distinct(StringComparer.Ordinal);
            var score = 0;
            foreach (var term in terms)
            {
                if (haystack.Contains(term, StringComparison.Ordinal))
                {
                    score += 1;
                }
            }

            return score;
        }
    }

    private static class KnowledgeSourceListParser
    {
        public static IReadOnlyList<string> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<string>();
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                var result = new List<string>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var value = item.ValueKind switch
                    {
                        JsonValueKind.String => item.GetString(),
                        JsonValueKind.Object => ReadObjectSourceCode(item),
                        _ => null
                    };

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value.Trim());
                    }
                }

                return result
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        private static string? ReadObjectSourceCode(JsonElement element)
        {
            foreach (var propertyName in new[] { "code", "sourceCode", "knowledgeSourceCode" })
            {
                if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }

            return null;
        }
    }
}
