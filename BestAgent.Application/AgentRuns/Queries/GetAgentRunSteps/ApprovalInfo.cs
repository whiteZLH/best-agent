namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record ApprovalInfo(
    string WaitType,
    string RequestedAction,
    string? RequestPayload,
    string SideEffectLevel,
    string Decision,
    string? Comment,
    DateTime? DecidedAt,
    string? ApprovalId = null,
    string? ApproverId = null,
    string? ApproverName = null,
    string? ApproverRole = null)
{
    public string ToolName => RequestedAction;

    public string? ToolInput => RequestPayload;
}
