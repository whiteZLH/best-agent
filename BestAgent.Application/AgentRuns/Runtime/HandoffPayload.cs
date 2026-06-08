using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public record HandoffPayload(
    string WaitType,
    string WaitToken,
    string TargetAgent,
    string? HandoffInput,
    string Mode,
    string ChildRunId,
    string Decision,
    string? ChildStatus,
    string? ChildOutput,
    string? Comment,
    DateTime? DecidedAt,
    string? RouteRuleId,
    string? ContextScope,
    string? MemoryScope,
    string? ToolScope,
    string? KnowledgeScope,
    bool ApprovalRequired,
    string? Reason,
    double? Confidence,
    string? ContextOverrides,
    string? MemoryOverrides,
    string? ToolOverrides,
    string? MergeStrategy = null);

public static class HandoffPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static HandoffPayload CreatePending(
        string waitToken,
        string targetAgent,
        string? handoffInput,
        string mode,
        string childRunId,
        string? routeRuleId = null,
        string? contextScope = null,
        string? memoryScope = null,
        string? toolScope = null,
        string? knowledgeScope = null,
        bool approvalRequired = false,
        string? reason = null,
        double? confidence = null,
        string? contextOverrides = null,
        string? memoryOverrides = null,
        string? toolOverrides = null,
        string? mergeStrategy = null)
    {
        var normalizedMode = NormalizeMode(mode);
        return new HandoffPayload(
            "handoff",
            NormalizeRequired(waitToken, nameof(waitToken)),
            targetAgent,
            handoffInput,
            normalizedMode,
            childRunId,
            ApprovalDecisions.Pending,
            null,
            null,
            null,
            null,
            routeRuleId,
            contextScope,
            memoryScope,
            toolScope,
            knowledgeScope,
            approvalRequired,
            Normalize(reason),
            confidence,
            Normalize(contextOverrides),
            Normalize(memoryOverrides),
            Normalize(toolOverrides),
            NormalizeMergeStrategy(normalizedMode, mergeStrategy));
    }

    public static HandoffPayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Handoff payload is missing.");
        }

        var handoffPayload = JsonSerializer.Deserialize<HandoffPayload>(payload, JsonOptions);
        if (handoffPayload is null
            || !string.Equals(handoffPayload.WaitType, "handoff", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(handoffPayload.WaitToken)
            || string.IsNullOrWhiteSpace(handoffPayload.TargetAgent)
            || string.IsNullOrWhiteSpace(handoffPayload.ChildRunId))
        {
            throw new InvalidOperationException("Handoff payload is invalid.");
        }

        return handoffPayload with
        {
            Mode = NormalizeMode(handoffPayload.Mode),
            MergeStrategy = NormalizeMergeStrategy(handoffPayload.Mode, handoffPayload.MergeStrategy)
        };
    }

    public static bool TryParse(string? payload, out HandoffPayload? handoffPayload)
    {
        handoffPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            handoffPayload = Parse(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Serialize(HandoffPayload payload)
    {
        return JsonSerializer.Serialize(payload with
        {
            Mode = NormalizeMode(payload.Mode),
            MergeStrategy = NormalizeMergeStrategy(payload.Mode, payload.MergeStrategy)
        });
    }

    public static string? NormalizeMergeStrategy(string? mode, string? mergeStrategy)
    {
        var normalizedMode = NormalizeMode(mode);
        if (!string.Equals(normalizedMode, "delegate_and_merge", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = Normalize(mergeStrategy);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "supervisor_summary";
        }

        return normalized switch
        {
            "supervisor_summary" => "supervisor_summary",
            "first_success" => "first_success",
            "all_results" => "all_results",
            _ => throw new InvalidOperationException($"Unsupported handoff merge strategy '{normalized}'.")
        };
    }

    public static HandoffPayload MarkCompleted(
        HandoffPayload payload,
        string childStatus,
        string? childOutput)
    {
        return payload with
        {
            Decision = ApprovalDecisions.Approved,
            ChildStatus = Normalize(childStatus),
            ChildOutput = childOutput,
            DecidedAt = DateTime.UtcNow
        };
    }

    public static HandoffPayload MarkFailed(
        HandoffPayload payload,
        string childStatus,
        string? comment)
    {
        return payload with
        {
            Decision = ApprovalDecisions.Rejected,
            ChildStatus = Normalize(childStatus),
            Comment = Normalize(comment),
            DecidedAt = DateTime.UtcNow
        };
    }

    public static HandoffPayload MarkApproved(HandoffPayload payload, string? comment = null)
    {
        return payload with
        {
            Decision = ApprovalDecisions.Approved,
            Comment = string.IsNullOrWhiteSpace(comment) ? payload.Comment : Normalize(comment),
            DecidedAt = DateTime.UtcNow
        };
    }

    public static HandoffPayload MarkRejected(HandoffPayload payload, string? comment)
    {
        return payload with
        {
            Decision = ApprovalDecisions.Rejected,
            Comment = Normalize(comment),
            DecidedAt = DateTime.UtcNow
        };
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = Normalize(mode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "delegate_and_wait";
        }

        return normalized switch
        {
            "route_only" => "route_only",
            "delegate_and_wait" => "delegate_and_wait",
            "delegate_and_merge" => "delegate_and_merge",
            _ => throw new InvalidOperationException($"Unsupported handoff mode '{normalized}'.")
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"Handoff payload field '{fieldName}' is required.");
        }

        return normalized;
    }
}
