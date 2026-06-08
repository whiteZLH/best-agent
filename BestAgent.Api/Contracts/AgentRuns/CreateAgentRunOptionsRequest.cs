namespace BestAgent.Api.Contracts.AgentRuns;

public record CreateAgentRunOptionsRequest(
    bool? Stream = null,
    int? MaxRounds = null);
