using BestAgent.Domain.Common;

namespace BestAgent.Domain.Knowledge;

public record class UserMemory : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string MemoryKey { get; init; } = string.Empty;
    public string MemoryScope { get; init; } = string.Empty;
    public string MemoryType { get; init; } = string.Empty;
    public string? MemoryValue { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string SourceRef { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
    public DateTime? EffectiveAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
