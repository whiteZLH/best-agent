namespace BestAgent.Application.AgentRuns.Services;

public sealed record RuntimeRequest(
    string AgentCode,
    string SessionId,
    string UserId,
    string IdempotencyKey,
    string InputText);
