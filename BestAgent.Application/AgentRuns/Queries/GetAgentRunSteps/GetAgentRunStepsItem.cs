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
    HandoffInfo? Handoff,
    ApprovalInfo? Approval,
    HumanWaitInfo? HumanWait,
    ToolInvocationInfo? ToolInvocation,
    ModelCallInfo? ModelCall,
    ModelFailureInfo? ModelFailure,
    ToolFailureInfo? ToolFailure,
    DateTime CreateTime,
    DateTime LastModifyTime,
    DateTime? StartedAt,
    DateTime? EndedAt,
    long DurationMs);
