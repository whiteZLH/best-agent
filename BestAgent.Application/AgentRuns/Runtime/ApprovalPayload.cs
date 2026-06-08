using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public record ApprovalPayload(
    string WaitType,
    string ToolName,
    string? ToolInput,
    string SideEffectLevel,
    string Decision,
    string? Comment,
    DateTime? DecidedAt);

public static class ApprovalPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ApprovalPayload CreatePending(string toolName, string? toolInput, string sideEffectLevel, string? comment = null)
    {
        return new ApprovalPayload(
            "approval",
            toolName,
            toolInput,
            sideEffectLevel,
            ApprovalDecisions.Pending,
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            null);
    }

    public static ApprovalPayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Approval payload is missing.");
        }

        var approvalPayload = JsonSerializer.Deserialize<ApprovalPayload>(payload, JsonOptions);
        if (approvalPayload is null || !string.Equals(approvalPayload.WaitType, "approval", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(approvalPayload.ToolName))
        {
            throw new InvalidOperationException("Approval payload is invalid.");
        }

        return approvalPayload;
    }

    public static bool TryParse(string? payload, out ApprovalPayload? approvalPayload)
    {
        approvalPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            approvalPayload = Parse(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Serialize(ApprovalPayload payload)
    {
        return JsonSerializer.Serialize(payload);
    }

    public static ApprovalPayload MarkApproved(ApprovalPayload payload, string? comment = null)
    {
        return payload with
        {
            Decision = ApprovalDecisions.Approved,
            Comment = string.IsNullOrWhiteSpace(comment) ? payload.Comment : comment,
            DecidedAt = DateTime.UtcNow
        };
    }

    public static ApprovalPayload MarkRejected(ApprovalPayload payload, string? comment)
    {
        return payload with
        {
            Decision = ApprovalDecisions.Rejected,
            Comment = comment,
            DecidedAt = DateTime.UtcNow
        };
    }
}

public static class ApprovalDecisions
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
