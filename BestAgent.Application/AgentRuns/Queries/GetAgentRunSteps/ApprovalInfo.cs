namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record ApprovalInfo(
    string WaitType,
    string ToolName,
    string? ToolInput,
    string SideEffectLevel,
    string Decision,
    string? Comment,
    DateTime? DecidedAt,
    string? ApprovalId = null,
    string? ApproverId = null,
    string? ApproverName = null,
    string? ApproverRole = null);
