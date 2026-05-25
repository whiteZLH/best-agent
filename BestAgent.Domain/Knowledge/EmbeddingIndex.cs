using BestAgent.Domain.Common;

namespace BestAgent.Domain.Knowledge;

public record class EmbeddingIndex : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string VectorRef { get; init; } = string.Empty;
    public string? VectorPayload { get; init; }
    public string? Metadata { get; init; }
}
