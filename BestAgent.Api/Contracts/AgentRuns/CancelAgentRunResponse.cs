namespace BestAgent.Api.Contracts.AgentRuns;

public record CancelAgentRunResponse(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null,
    string? Reason = null);
