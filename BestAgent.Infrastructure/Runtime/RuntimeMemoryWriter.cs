using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Knowledge;
using Microsoft.Extensions.Logging;

namespace BestAgent.Infrastructure.Runtime;

public class RuntimeMemoryWriter : IRuntimeMemoryWriter
{
    private const int ToolInputLimit = 600;
    private const int ToolOutputLimit = 1200;
    private const int UserInputLimit = 600;
    private const int FinalOutputLimit = 1600;
    private const int MemoryKeyLimit = 128;
    private const int MemoryValueLimit = 600;

    private readonly ISessionMemoryRepository _sessionMemoryRepository;
    private readonly IUserMemoryRepository _userMemoryRepository;
    private readonly ISummaryMemoryRepository _summaryMemoryRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly ILogger<RuntimeMemoryWriter> _logger;

    public RuntimeMemoryWriter(
        ISessionMemoryRepository sessionMemoryRepository,
        IUserMemoryRepository userMemoryRepository,
        ISummaryMemoryRepository summaryMemoryRepository,
        IAgentStepRepository agentStepRepository,
        ILogger<RuntimeMemoryWriter> logger)
    {
        _sessionMemoryRepository = sessionMemoryRepository;
        _userMemoryRepository = userMemoryRepository;
        _summaryMemoryRepository = summaryMemoryRepository;
        _agentStepRepository = agentStepRepository;
        _logger = logger;
    }

    public async Task RecordToolResultAsync(
        AgentRun run,
        string toolName,
        string? toolInput,
        string? toolOutput,
        bool persistSessionMemory,
        bool persistUserMemory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolOutput)
            || (!persistSessionMemory && !persistUserMemory))
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            if (persistSessionMemory && !string.IsNullOrWhiteSpace(run.SessionId))
            {
                var contentJson = JsonSerializer.Serialize(new
                {
                    kind = "tool_result",
                    toolName = toolName.Trim(),
                    toolInput = NormalizeSnippet(RuntimePayloadMasker.MaskToolInput(toolInput), ToolInputLimit),
                    toolOutput = NormalizeSnippet(RuntimePayloadMasker.MaskToolOutput(toolOutput), ToolOutputLimit),
                    recordedAt = now
                });

                await _sessionMemoryRepository.AddAsync(
                    new SessionMemory
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        TenantId = run.TenantId,
                        UserId = run.UserId,
                        SessionId = run.SessionId,
                        RunId = run.RunId,
                        MemoryType = "tool_result",
                        ContentJson = contentJson,
                        SourceType = "tool_result",
                        SourceRef = BuildSourceRef(run.RunId, toolName),
                        Confidence = 1.0m,
                        ExpiresAt = now.AddHours(12),
                        Creator = "system",
                        CreatorName = "system",
                        LastModifier = "system",
                        LastModifierName = "system",
                        CreateTime = now,
                        LastModifyTime = now
                    },
                    cancellationToken);
            }

            if (persistUserMemory)
            {
                await PersistUserMemoriesAsync(run, toolName, toolOutput, now, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist tool result memory for run {RunId}", run.RunId);
        }
    }

    public async Task RecordRunCompletionSummaryAsync(
        AgentRun run,
        string? finalOutput,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(finalOutput))
        {
            return;
        }

        try
        {
            var steps = await _agentStepRepository.ListByRunIdAsync(run.RunId, cancellationToken);
            var orderedSteps = steps
                .Where(step => !step.Deleted)
                .OrderBy(step => step.StepNo)
                .ToArray();
            var sourceStartSeq = orderedSteps.Length == 0 ? 0 : orderedSteps[0].StepNo;
            var sourceEndSeq = orderedSteps.Length == 0 ? run.CurrentStepNo : orderedSteps[^1].StepNo;
            var completedStepCount = orderedSteps.Count(step =>
                string.Equals(step.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var now = DateTime.UtcNow;
            var normalizedInput = NormalizeSnippet(run.InputPayload, UserInputLimit);
            var normalizedOutput = NormalizeSnippet(finalOutput, FinalOutputLimit);
            var summaryText = BuildSummaryText(normalizedInput, normalizedOutput, completedStepCount);
            var summaryJson = JsonSerializer.Serialize(new
            {
                kind = "run_completion",
                input = normalizedInput,
                output = normalizedOutput,
                completedStepCount,
                sourceStartSeq,
                sourceEndSeq,
                generatedAt = now
            });

            await _summaryMemoryRepository.AddAsync(
                new SummaryMemory
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TenantId = run.TenantId,
                    RunId = run.RunId,
                    SessionId = run.SessionId,
                    SummaryType = "run_completion",
                    SourceStartSeq = sourceStartSeq,
                    SourceEndSeq = sourceEndSeq,
                    SummaryText = summaryText,
                    SummaryJson = summaryJson,
                    GeneratedByModel = "runtime_template",
                    GeneratedAt = now,
                    ExpiresAt = now.AddDays(7),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist completion summary for run {RunId}", run.RunId);
        }
    }

    private static string BuildSummaryText(string input, string output, int completedStepCount)
    {
        return
            $"""
            User request:
            {input}

            Final outcome:
            {output}

            Completed steps: {completedStepCount}
            """.Trim();
    }

    private static string BuildSourceRef(string runId, string toolName)
    {
        var normalizedToolName = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName.Trim();
        var sourceRef = $"{runId}:{normalizedToolName}";
        return sourceRef.Length <= 128 ? sourceRef : sourceRef[..128];
    }

    private static string NormalizeSnippet(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }

    private async Task PersistUserMemoriesAsync(
        AgentRun run,
        string toolName,
        string toolOutput,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.UserId))
        {
            return;
        }

        IReadOnlyList<UserMemoryCandidate> candidates;
        try
        {
            candidates = ExtractUserMemoryCandidates(toolOutput, recordedAt);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Tool output for run {RunId} does not contain structured user memories", run.RunId);
            return;
        }

        foreach (var candidate in candidates)
        {
            var existing = await _userMemoryRepository.GetByMemoryKeyAsync(
                run.TenantId,
                run.UserId,
                candidate.MemoryKey,
                cancellationToken);

            if (existing is null)
            {
                await _userMemoryRepository.AddAsync(
                    CreateUserMemory(run, toolName, candidate, recordedAt),
                    cancellationToken);
                continue;
            }

            await _userMemoryRepository.UpdateAsync(
                existing with
                {
                    MemoryScope = candidate.MemoryScope,
                    MemoryType = candidate.MemoryType,
                    MemoryValue = candidate.MemoryValue,
                    SourceType = "tool_memory",
                    SourceRef = BuildSourceRef(run.RunId, toolName),
                    Confidence = candidate.Confidence,
                    EffectiveAt = candidate.EffectiveAt,
                    ExpiresAt = candidate.ExpiresAt,
                    LastModifier = "system",
                    LastModifierName = "system",
                    LastModifyTime = recordedAt
                },
                cancellationToken);
        }
    }

    private static UserMemory CreateUserMemory(
        AgentRun run,
        string toolName,
        UserMemoryCandidate candidate,
        DateTime recordedAt)
    {
        return new UserMemory
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = run.TenantId,
            UserId = run.UserId,
            MemoryKey = candidate.MemoryKey,
            MemoryScope = candidate.MemoryScope,
            MemoryType = candidate.MemoryType,
            MemoryValue = candidate.MemoryValue,
            SourceType = "tool_memory",
            SourceRef = BuildSourceRef(run.RunId, toolName),
            Confidence = candidate.Confidence,
            EffectiveAt = candidate.EffectiveAt,
            ExpiresAt = candidate.ExpiresAt,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = recordedAt,
            LastModifyTime = recordedAt
        };
    }

    private static IReadOnlyList<UserMemoryCandidate> ExtractUserMemoryCandidates(string toolOutput, DateTime recordedAt)
    {
        using var document = JsonDocument.Parse(toolOutput);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("userMemories", out var userMemoriesElement)
            || userMemoriesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<UserMemoryCandidate>();
        }

        var candidates = new List<UserMemoryCandidate>();
        foreach (var item in userMemoriesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var memoryKey = ReadString(item, "memoryKey");
            var memoryValue = ReadString(item, "memoryValue");
            if (string.IsNullOrWhiteSpace(memoryKey) || string.IsNullOrWhiteSpace(memoryValue))
            {
                continue;
            }

            var memoryScope = ReadString(item, "memoryScope");
            var memoryType = ReadString(item, "memoryType");
            var effectiveAt = ReadDateTime(item, "effectiveAt") ?? recordedAt;
            var expiresAt = ResolveExpiresAt(item, effectiveAt);
            var confidence = ReadDecimal(item, "confidence") ?? 1.0m;
            if (expiresAt != null && expiresAt <= effectiveAt)
            {
                continue;
            }

            candidates.Add(new UserMemoryCandidate(
                NormalizeSnippet(memoryKey, MemoryKeyLimit),
                string.IsNullOrWhiteSpace(memoryScope) ? "user" : NormalizeSnippet(memoryScope, 64),
                string.IsNullOrWhiteSpace(memoryType) ? "fact" : NormalizeSnippet(memoryType, 32),
                NormalizeSnippet(memoryValue, MemoryValueLimit),
                Math.Clamp(confidence, 0m, 1.0m),
                effectiveAt,
                expiresAt));
        }

        return candidates;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static DateTime? ResolveExpiresAt(JsonElement element, DateTime effectiveAt)
    {
        var absolute = ReadDateTime(element, "expiresAt");
        if (absolute != null)
        {
            return absolute;
        }

        var ttlSeconds = ReadDouble(element, "ttlSeconds")
            ?? ReadDouble(element, "ttl_seconds")
            ?? ReadDouble(element, "ttl");
        if (ttlSeconds == null || ttlSeconds <= 0)
        {
            return null;
        }

        try
        {
            return effectiveAt.AddSeconds(ttlSeconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTime? ReadDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTime.TryParse(property.GetString(), out var value)
            ? value.ToUniversalTime()
            : null;
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numericValue))
        {
            return numericValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var numericValue))
        {
            return numericValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && double.TryParse(property.GetString(), out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private sealed record UserMemoryCandidate(
        string MemoryKey,
        string MemoryScope,
        string MemoryType,
        string MemoryValue,
        decimal Confidence,
        DateTime EffectiveAt,
        DateTime? ExpiresAt);
}
