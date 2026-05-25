using BestAgent.Domain.Common;

namespace BestAgent.Domain.Tools;

public sealed class ToolInvocation : AuditedEntity
{
    public string InvocationId { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public string StepId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public ToolInvocationStatus Status { get; set; } = ToolInvocationStatus.Pending;

    public string InputPayload { get; set; } = string.Empty;

    public string? OutputPayload { get; set; }

    public string? ErrorPayload { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }
}
