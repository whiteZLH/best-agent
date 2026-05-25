using BestAgent.Domain.Common;

namespace BestAgent.Domain.Tools;

public record class ToolInvocation : AuditedEntity
{
    public string InvocationId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? InputPayload { get; init; }
    public string? OutputPayload { get; init; }
    public string? ErrorPayload { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string CallbackToken { get; init; } = string.Empty;
    public string ExecutorNode { get; init; } = string.Empty;
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public long DurationMs { get; init; }
}
