namespace BestAgent.Api.Contracts.AgentRuns;

public record RequestHumanAgentRunResponse(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken);
