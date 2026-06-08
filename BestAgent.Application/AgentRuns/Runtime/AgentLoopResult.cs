namespace BestAgent.Application.AgentRuns.Runtime;

public abstract record AgentLoopResult;

public sealed record AgentLoopCompleted(
    string Output,
    string? ModelInput = null,
    decimal TotalCostDelta = 0m) : AgentLoopResult;

public sealed record AgentLoopSuspended(
    string WaitToken,
    int SuspendedAtStepNo,
    string StepId,
    string InvocationId,
    string ToolName,
    string? ToolInput,
    decimal TotalCostDelta = 0m) : AgentLoopResult;

public sealed record AgentLoopWaitingApproval(
    string WaitToken,
    int SuspendedAtStepNo,
    string StepId,
    string ToolName,
    string? ToolInput,
    string SideEffectLevel,
    string StepType = "tool_call",
    decimal TotalCostDelta = 0m) : AgentLoopResult;

public sealed record AgentLoopWaitingHuman(
    string WaitToken,
    int SuspendedAtStepNo,
    string? SourceStepId,
    string? SourceInvocationId,
    string? ToolName,
    string? ToolInput,
    string? SourceToolOutput,
    string Comment,
    string SourceType,
    string? SourceToolStatus,
    bool ContinueAsToolResult,
    decimal TotalCostDelta = 0m) : AgentLoopResult;

public sealed record AgentLoopWaitingHandoff(
    string WaitToken,
    int SuspendedAtStepNo,
    string StepId,
    string TargetAgent,
    string? HandoffInput,
    string HandoffMode,
    string ChildRunId,
    decimal TotalCostDelta = 0m) : AgentLoopResult;

public sealed record AgentLoopFailed(
    int FailedAtStepNo,
    string StepType,
    string ErrorPayload,
    string ErrorMessage,
    decimal TotalCostDelta = 0m) : AgentLoopResult;
