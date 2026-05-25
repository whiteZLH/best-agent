namespace BestAgent.Application.AgentRuns;

public sealed record AgentRunModel(
    string RunId,
    string AgentCode,
    string Status,
    string? Output,
    string? ErrorMessage,
    int CurrentStepNo,
    string IdempotencyKey);
