using BestAgent.Domain.Common;

namespace BestAgent.Domain.Idempotency;

public sealed class IdempotencyRecord : AuditedEntity
{
    public string Id { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }
}
