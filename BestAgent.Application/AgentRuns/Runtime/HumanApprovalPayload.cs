using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public record HumanApprovalPayload(
    string WaitType,
    string Decision,
    string? Comment,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole,
    string? HumanResult,
    DateTime? DecidedAt,
    string? SourceType = null,
    string? SourceStepId = null,
    string? SourceInvocationId = null,
    string? SourceToolName = null,
    string? SourceToolInput = null,
    string? SourceToolOutput = null,
    string? SourceToolStatus = null,
    bool ContinueAsToolResult = false);

public static class HumanApprovalPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static HumanApprovalPayload CreatePending(
        string? comment,
        string? sourceType = null,
        string? sourceStepId = null,
        string? sourceInvocationId = null,
        string? sourceToolName = null,
        string? sourceToolInput = null,
        string? sourceToolOutput = null,
        string? sourceToolStatus = null,
        bool continueAsToolResult = false)
    {
        return new HumanApprovalPayload(
            "human",
            ApprovalDecisions.Pending,
            comment,
            null,
            null,
            null,
            null,
            null,
            Normalize(sourceType),
            Normalize(sourceStepId),
            Normalize(sourceInvocationId),
            Normalize(sourceToolName),
            sourceToolInput,
            sourceToolOutput,
            Normalize(sourceToolStatus),
            continueAsToolResult);
    }

    public static HumanApprovalPayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Human payload is missing.");
        }

        var humanPayload = JsonSerializer.Deserialize<HumanApprovalPayload>(payload, JsonOptions);
        if (humanPayload is null
            || !string.Equals(humanPayload.WaitType, "human", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Human payload is invalid.");
        }

        return humanPayload;
    }

    public static bool TryParse(string? payload, out HumanApprovalPayload? humanPayload)
    {
        humanPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            humanPayload = Parse(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Serialize(HumanApprovalPayload payload)
    {
        return JsonSerializer.Serialize(payload);
    }

    public static HumanApprovalPayload MarkCompleted(
        HumanApprovalPayload payload,
        string? humanResult,
        string? comment,
        string? humanOperatorId,
        string? humanOperatorName,
        string? humanOperatorRole)
    {
        return payload with
        {
            Decision = ApprovalDecisions.Approved,
            HumanResult = humanResult,
            Comment = string.IsNullOrWhiteSpace(comment) ? payload.Comment : comment,
            HumanOperatorId = Normalize(humanOperatorId),
            HumanOperatorName = Normalize(humanOperatorName),
            HumanOperatorRole = Normalize(humanOperatorRole),
            DecidedAt = DateTime.UtcNow
        };
    }

    public static HumanApprovalPayload MarkTerminated(
        HumanApprovalPayload payload,
        string? comment,
        string? humanOperatorId,
        string? humanOperatorName,
        string? humanOperatorRole)
    {
        return payload with
        {
            Decision = ApprovalDecisions.Rejected,
            Comment = string.IsNullOrWhiteSpace(comment) ? "Terminated by human operator." : comment.Trim(),
            HumanOperatorId = Normalize(humanOperatorId),
            HumanOperatorName = Normalize(humanOperatorName),
            HumanOperatorRole = Normalize(humanOperatorRole),
            DecidedAt = DateTime.UtcNow
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
