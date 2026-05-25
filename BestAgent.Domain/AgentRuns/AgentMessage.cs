using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentRuns;

public record class AgentMessage : AuditedEntity
{
    public string MessageId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string MessageType { get; init; } = string.Empty;
    public long SeqNo { get; init; }
    public string? Content { get; init; }
    public string? ContentJson { get; init; }
    public string SourceRef { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
