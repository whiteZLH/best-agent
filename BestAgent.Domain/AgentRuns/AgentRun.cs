using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentRuns;

public record class AgentRun : AuditedEntity
{
    public string RunId { get; init; } = string.Empty;

    public string AgentCode { get; init; } = string.Empty;

    public string AgentDefinitionVersionId { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? InputPayload { get; init; }

    public string? OutputPayload { get; init; }

    public int CurrentStepNo { get; init; }

    public string ParentRunId { get; init; } = string.Empty;

    public string RootRunId { get; init; } = string.Empty;

    public string DelegatedByRunId { get; init; } = string.Empty;

    public string DelegatedByAgent { get; init; } = string.Empty;

    public long StatusVersion { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string CurrentWaitToken { get; init; } = string.Empty;

    public string InterruptReason { get; init; } = string.Empty;

    public int MaxTurns { get; init; }

    public decimal MaxCost { get; init; }

    public decimal TotalCost { get; init; }

    public DateTime? StartedAt { get; init; }

    public DateTime? EndedAt { get; init; }

    public DateTime? LastHeartbeatAt { get; init; }
}
