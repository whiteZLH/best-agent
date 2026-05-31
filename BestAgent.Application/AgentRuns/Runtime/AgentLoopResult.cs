namespace BestAgent.Application.AgentRuns.Runtime;

public abstract record AgentLoopResult;

public sealed record AgentLoopCompleted(string Output) : AgentLoopResult;

public sealed record AgentLoopSuspended(string WaitToken, int SuspendedAtStepNo) : AgentLoopResult;

public sealed record AgentLoopWaitingApproval(
    string WaitToken,
    int SuspendedAtStepNo,
    string StepId,
    string ToolName,
    string? ToolInput,
    string SideEffectLevel) : AgentLoopResult;
