namespace BestAgent.Application.AgentRuns.Runtime;

public abstract record AgentLoopResult;

public sealed record AgentLoopCompleted(string Output) : AgentLoopResult;

public sealed record AgentLoopSuspended(string WaitToken, int SuspendedAtStepNo) : AgentLoopResult;
