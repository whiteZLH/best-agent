using BestAgent.Domain.Common;

namespace BestAgent.Domain.Knowledge;

public record class SessionMemory : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string MemoryType { get; init; } = string.Empty;
    public string? ContentJson { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string SourceRef { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
