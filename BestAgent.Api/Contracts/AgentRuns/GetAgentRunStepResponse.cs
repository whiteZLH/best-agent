namespace BestAgent.Api.Contracts.AgentRuns;

public record GetAgentRunStepResponse(
    string StepId,
    int StepNo,
    string StepType,
    string Status,
    string? Input,
    string? Output,
    string? Error,
    string StepKey,
    HandoffInfoResponse? Handoff,
    ApprovalInfoResponse? Approval,
    HumanWaitInfoResponse? HumanWait,
    ToolInvocationInfoResponse? ToolInvocation,
    ModelCallInfoResponse? ModelCall,
    ModelFailureInfoResponse? ModelFailure,
    ToolFailureInfoResponse? ToolFailure,
    DateTime CreateTime,
    DateTime LastModifyTime,
    DateTime? StartedAt,
    DateTime? EndedAt,
    long DurationMs);
