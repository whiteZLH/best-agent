namespace BestAgent.Api.Contracts.AgentRuns;

public record CreateAgentRunRequest(
    string AgentCode,
    string Input,
    string? IdempotencyKey = null,
    string? TenantId = null,
    string? UserId = null,
    string? SessionId = null,
    CreateAgentRunOptionsRequest? Options = null);
