namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;

using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

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
    string? WaitToken,
    string? CurrentStepId = null,
    string? WaitStepType = null,
    string? CurrentInvocationId = null,
    string? CurrentApprovalId = null,
    ToolInvocationInfo? CurrentToolInvocation = null,
    ApprovalInfo? CurrentApproval = null,
    HumanWaitInfo? CurrentHumanWait = null,
    HandoffInfo? CurrentHandoff = null);
