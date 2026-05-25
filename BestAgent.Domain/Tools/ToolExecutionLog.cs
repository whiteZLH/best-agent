using BestAgent.Domain.Common;

namespace BestAgent.Domain.Tools;

public record class ToolExecutionLog : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string InvocationId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string? RequestPayload { get; init; }
    public string? ResponsePayload { get; init; }
    public long LatencyMs { get; init; }
    public bool SuccessFlag { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}
