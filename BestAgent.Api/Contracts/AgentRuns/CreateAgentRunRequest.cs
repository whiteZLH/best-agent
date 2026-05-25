namespace BestAgent.Api.Contracts.AgentRuns;

public record CreateAgentRunRequest(
    string AgentCode,
    string Input);
