using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentRuns;

public record class AgentStep : AuditedEntity
{
    public string StepId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public int StepNo { get; init; }
    public string StepType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? InputPayload { get; init; }
    public string? OutputPayload { get; init; }
    public string? ErrorPayload { get; init; }
    public string StepKey { get; init; } = string.Empty;
    public int RetryCount { get; init; }
    public string DependsOnStepId { get; init; } = string.Empty;
    public string? DecisionPayload { get; init; }
    public long StatusVersion { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public long DurationMs { get; init; }
}
