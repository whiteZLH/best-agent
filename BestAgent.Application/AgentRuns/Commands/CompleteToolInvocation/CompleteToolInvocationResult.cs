namespace BestAgent.Application.AgentRuns.Commands.CompleteToolInvocation;

public record CompleteToolInvocationResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
