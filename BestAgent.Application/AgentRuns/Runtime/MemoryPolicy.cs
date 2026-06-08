using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public sealed record MemoryPolicy(
    bool ToolResultMemoryEnabled,
    IReadOnlySet<string> AllowedToolNames,
    bool UserMemoryWriteEnabled,
    bool SummaryMemoryWriteEnabled,
    bool IncludeSummary,
    bool IncludeKnowledge,
    int MaxKnowledgeChunks,
    int KnowledgeCandidateCount,
    bool IncludeSessionMemory,
    int MaxSessionMemories,
    bool IncludeUserMemory,
    int MaxUserMemories)
{
    public static MemoryPolicy Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var toolResultMemoryEnabled = ReadBoolean(root, "toolResultMemoryEnabled")
                ?? ReadBoolean(root, "includeToolResultMemory")
                ?? ReadBoolean(root, "enableToolResultMemory")
                ?? true;
            var allowedToolNames = ReadStringSet(
                root,
                "toolResultMemoryAllowedTools",
                "trustedToolNames",
                "memoryWriteAllowedTools",
                "trustedMemoryToolNames");
            var userMemoryWriteEnabled = ReadBoolean(root, "userMemoryWriteEnabled")
                ?? ReadBoolean(root, "includeUserMemoryWrite")
                ?? ReadBoolean(root, "enableUserMemoryWrite")
                ?? toolResultMemoryEnabled;
            var summaryMemoryWriteEnabled = ReadBoolean(root, "summaryMemoryWriteEnabled")
                ?? ReadBoolean(root, "includeSummaryWrite")
                ?? ReadBoolean(root, "enableSummaryWrite")
                ?? true;
            var includeSummary = ReadBoolean(root, "includeSummary")
                ?? ReadBoolean(root, "summaryEnabled")
                ?? ReadBoolean(root, "enableSummary")
                ?? true;
            var includeKnowledge = ReadBoolean(root, "includeKnowledge")
                ?? ReadBoolean(root, "knowledgeEnabled")
                ?? ReadBoolean(root, "enableKnowledge")
                ?? true;
            var maxKnowledgeChunks = ReadInt32(root, "maxKnowledgeChunks")
                ?? ReadInt32(root, "knowledgeTopK")
                ?? ReadInt32(root, "retrievalTopK")
                ?? 3;
            var knowledgeCandidateCount = ReadInt32(root, "knowledgeCandidateCount")
                ?? ReadInt32(root, "retrievalCandidateCount")
                ?? ReadInt32(root, "knowledgeRecallTopK")
                ?? Math.Max(maxKnowledgeChunks, maxKnowledgeChunks * 3);
            var includeSessionMemory = ReadBoolean(root, "includeSessionMemory")
                ?? ReadBoolean(root, "sessionMemoryEnabled")
                ?? ReadBoolean(root, "enableSessionMemory")
                ?? true;
            var maxSessionMemories = ReadInt32(root, "maxSessionMemories")
                ?? ReadInt32(root, "sessionMemoryTopK")
                ?? 3;
            var includeUserMemory = ReadBoolean(root, "includeUserMemory")
                ?? ReadBoolean(root, "userMemoryEnabled")
                ?? ReadBoolean(root, "enableUserMemory")
                ?? true;
            var maxUserMemories = ReadInt32(root, "maxUserMemories")
                ?? ReadInt32(root, "userMemoryTopK")
                ?? 3;

            return new MemoryPolicy(
                toolResultMemoryEnabled,
                allowedToolNames,
                userMemoryWriteEnabled,
                summaryMemoryWriteEnabled,
                includeSummary,
                includeKnowledge,
                Math.Max(0, maxKnowledgeChunks),
                Math.Max(Math.Max(0, maxKnowledgeChunks), knowledgeCandidateCount),
                includeSessionMemory,
                Math.Max(0, maxSessionMemories),
                includeUserMemory,
                Math.Max(0, maxUserMemories));
        }
        catch (JsonException)
        {
            return Default();
        }
    }

    public bool AllowsToolResultWrite(string? toolName)
    {
        if (!ToolResultMemoryEnabled)
        {
            return false;
        }

        if (AllowedToolNames.Count == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(toolName)
            && AllowedToolNames.Contains(toolName.Trim());
    }

    public bool AllowsUserMemoryWrite()
    {
        return UserMemoryWriteEnabled;
    }

    public bool AllowsSummaryMemoryWrite()
    {
        return SummaryMemoryWriteEnabled;
    }

    private static MemoryPolicy Default()
    {
        return new MemoryPolicy(
            ToolResultMemoryEnabled: true,
            AllowedToolNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            UserMemoryWriteEnabled: true,
            SummaryMemoryWriteEnabled: true,
            IncludeSummary: true,
            IncludeKnowledge: true,
            MaxKnowledgeChunks: 3,
            KnowledgeCandidateCount: 9,
            IncludeSessionMemory: true,
            MaxSessionMemories: 3,
            IncludeUserMemory: true,
            MaxUserMemories: 3);
    }

    private static bool? ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.True
            ? true
            : property.ValueKind == JsonValueKind.False
                ? false
                : null;
    }

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static IReadOnlySet<string> ReadStringSet(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return property
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
