namespace BestAgent.Api.Contracts.Tools;

public record UpdateToolDefinitionRequest(
    string DisplayName,
    string? Description,
    string? InputSchema,
    string? OutputSchema,
    string? EndpointUrl,
    string? HttpMethod,
    string? AuthHeaders,
    string SideEffectLevel,
    int TimeoutMs,
    string? RetryPolicy,
    string? AuthPolicy,
    string? IdempotencyPolicy,
    bool AsyncSupported,
    string ConsistencyMode,
    string? CompensationPolicy,
    bool Enabled);
