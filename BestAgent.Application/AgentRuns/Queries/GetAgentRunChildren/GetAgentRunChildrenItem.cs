namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;

public record GetAgentRunChildrenItem(
    string RunId,
    string AgentCode,
    string Status,
    string? Input,
    string? Output,
    DateTime CreateTime,
    DateTime LastModifyTime,
    DateTime? StartedAt,
    DateTime? EndedAt,
    int CurrentStepNo,
    string? ParentRunId,
    string? RootRunId,
    string? DelegatedByRunId,
    string? DelegatedByAgent,
    string? InterruptReason,
    string? WaitToken);
