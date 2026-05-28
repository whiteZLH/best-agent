using BestAgent.Domain.Common;

namespace BestAgent.Domain.Tools;

public record class ToolDefinition : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? InputSchema { get; init; }
    public string? OutputSchema { get; init; }
    public string? EndpointUrl { get; init; }
    public string HttpMethod { get; init; } = "POST";
    public string? AuthHeaders { get; init; }
    public string SideEffectLevel { get; init; } = string.Empty;
    public int TimeoutMs { get; init; }
    public string? RetryPolicy { get; init; }
    public string? AuthPolicy { get; init; }
    public string? IdempotencyPolicy { get; init; }
    public bool AsyncSupported { get; init; }
    public string ConsistencyMode { get; init; } = string.Empty;
    public string? CompensationPolicy { get; init; }
    public bool Enabled { get; init; }
}
