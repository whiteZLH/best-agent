namespace BestAgent.Api.Contracts.AgentRuns;

public record CreateAgentRunResponse(
    string RunId,
    string AgentCode,
    string? Input,
    string Status);
