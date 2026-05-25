using BestAgent.Domain.Common;

namespace BestAgent.Domain.Knowledge;

public record class SummaryMemory : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string SummaryType { get; init; } = string.Empty;
    public long SourceStartSeq { get; init; }
    public long SourceEndSeq { get; init; }
    public string SummaryText { get; init; } = string.Empty;
    public string? SummaryJson { get; init; }
    public string GeneratedByModel { get; init; } = string.Empty;
    public DateTime GeneratedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
