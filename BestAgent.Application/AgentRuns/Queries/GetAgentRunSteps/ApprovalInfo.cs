namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record ApprovalInfo(
    string WaitType,
    string ToolName,
    string? ToolInput,
    string SideEffectLevel,
    string Decision,
    string? Comment,
    DateTime? DecidedAt);
