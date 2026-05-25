using BestAgent.Domain.Common;

namespace BestAgent.Domain.Knowledge;

public record class KnowledgeChunk : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string DocumentId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public int ChunkNo { get; init; }
    public string Content { get; init; } = string.Empty;
    public int TokenCount { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? Metadata { get; init; }
}
