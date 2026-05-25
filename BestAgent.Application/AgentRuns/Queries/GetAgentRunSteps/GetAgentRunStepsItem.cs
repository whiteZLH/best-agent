namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record GetAgentRunStepsItem(
    string StepId,
    int StepNo,
    string StepType,
    string Status,
    string? Input,
    string? Output,
    string? Error,
    string StepKey,
    DateTime CreateTime,
    DateTime LastModifyTime,
    DateTime? StartedAt,
    DateTime? EndedAt,
    long DurationMs);
