namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunById;

public record GetAgentRunByIdResult(
    string RunId,
    string AgentCode,
    string Status,
    string? Input,
    string? Output,
    int MaxTurns,
    decimal MaxCost,
    decimal TotalCost,
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
