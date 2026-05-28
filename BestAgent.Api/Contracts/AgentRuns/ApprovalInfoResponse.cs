namespace BestAgent.Api.Contracts.AgentRuns;

public record ApprovalInfoResponse(
    string WaitType,
    string ToolName,
    string? ToolInput,
    string SideEffectLevel,
    string Decision,
    string? Comment,
    DateTime? DecidedAt);
