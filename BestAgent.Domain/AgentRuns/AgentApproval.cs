using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentRuns;

public record class AgentApproval : AuditedEntity
{
    public string ApprovalId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string RequestedAction { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public string? RequestPayload { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string ApproverId { get; init; } = string.Empty;
    public string ApproverRole { get; init; } = string.Empty;
    public string ApproverName { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
    public string WaitToken { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
    public DateTime? DecidedAt { get; init; }
}
