namespace BestAgent.Api.Contracts.AgentRuns;

public record CompleteToolInvocationResponse(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
