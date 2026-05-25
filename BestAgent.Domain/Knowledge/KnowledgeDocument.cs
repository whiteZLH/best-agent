using BestAgent.Domain.Common;

namespace BestAgent.Domain.Knowledge;

public record class KnowledgeDocument : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string KnowledgeSourceCode { get; init; } = string.Empty;
    public string DocumentCode { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string SourceUri { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string? Metadata { get; init; }
    public string ParseStatus { get; init; } = string.Empty;
    public int VersionNo { get; init; }
}
