namespace BestAgent.Api.Contracts.AgentRuns;

public record GetAgentRunResponse(
    string RunId,
    string AgentCode,
    string Status,
    string? Input,
    string? Output,
    int MaxTurns,
    decimal MaxCost,
    decimal TotalCost,
    DateTime CreateTime,
    DateTime LastModifyTime,
    DateTime? StartedAt,
    DateTime? EndedAt);
