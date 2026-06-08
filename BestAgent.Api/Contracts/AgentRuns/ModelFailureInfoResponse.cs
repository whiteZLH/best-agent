namespace BestAgent.Api.Contracts.AgentRuns;

public record ModelFailureInfoResponse(
    string? ErrorCode,
    string Message);
