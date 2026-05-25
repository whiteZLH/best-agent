using BestAgent.Domain.Common;

namespace BestAgent.Domain.Messages;

public sealed class AgentMessage : AuditedEntity
{
    public string MessageId { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
