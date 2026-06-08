namespace BestAgent.Application.AgentRuns.Runtime;

public record StepDecision(
    string Action,
    string? Response,
    string? ToolName,
    string? ToolInput,
    string? TargetAgent,
    string? HandoffInput,
    string? HandoffMode,
    string? HandoffReason,
    double? HandoffConfidence,
    string? HandoffContextOverrides,
    string? HandoffMemoryOverrides,
    string? HandoffToolOverrides,
    bool? HandoffApprovalRequired,
    string? HumanComment,
    string? FailErrorCode,
    string? FailMessage)
{
    public static StepDecision Respond(string response)
    {
        return new("respond", response, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    }

    public static StepDecision ToolCall(string toolName, string? toolInput)
    {
        return new("tool_call", null, toolName, toolInput, null, null, null, null, null, null, null, null, null, null, null, null);
    }

    public static StepDecision Handoff(
        string targetAgent,
        string? handoffInput,
        string? handoffMode,
        string? reason = null,
        double? confidence = null,
        string? contextOverrides = null,
        string? memoryOverrides = null,
        string? toolOverrides = null,
        bool? approvalRequired = null)
    {
        return new(
            "handoff",
            null,
            null,
            null,
            targetAgent,
            handoffInput,
            handoffMode,
            reason,
            confidence,
            contextOverrides,
            memoryOverrides,
            toolOverrides,
            approvalRequired,
            null,
            null,
            null);
    }

    public static StepDecision RequestHuman(string? comment)
    {
        return new("request_human", null, null, null, null, null, null, null, null, null, null, null, null, comment, null, null);
    }

    public static StepDecision Fail(string? errorCode, string errorMessage)
    {
        return new("fail", null, null, null, null, null, null, null, null, null, null, null, null, null, errorCode, errorMessage);
    }
}
