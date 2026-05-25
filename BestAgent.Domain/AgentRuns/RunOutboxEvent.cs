using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentRuns;

public record class RunOutboxEvent : AuditedEntity
{
    public string EventId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public long SeqNo { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string RunStatus { get; init; } = string.Empty;
    public string? Payload { get; init; }
    public string PublishStatus { get; init; } = string.Empty;
    public DateTime? PublishedAt { get; init; }
    public int RetryCount { get; init; }
    public DateTime OccurredAt { get; init; }
}
