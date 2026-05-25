using BestAgent.Domain.Common;

namespace BestAgent.Domain.Events;

public sealed class OutboxEvent : AuditedEntity
{
    public string EventId { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public int SequenceNo { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }
}
