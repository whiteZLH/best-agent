using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentRuns;

public record class IdempotencyRecord : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string ScopeType { get; init; } = string.Empty;
    public string ScopeKey { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? ExpireAt { get; init; }
    public string? ExtraPayload { get; init; }
}
