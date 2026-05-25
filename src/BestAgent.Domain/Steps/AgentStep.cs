using BestAgent.Domain.Common;

namespace BestAgent.Domain.Steps;

public sealed class AgentStep : AuditedEntity
{
    public string StepId { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public int StepNo { get; set; }

    public AgentStepType StepType { get; set; }

    public AgentStepStatus Status { get; set; } = AgentStepStatus.Succeeded;

    public string InputPayload { get; set; } = string.Empty;

    public string? OutputPayload { get; set; }

    public string? ErrorPayload { get; set; }

    public string StepKey { get; set; } = string.Empty;

    public int RetryCount { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset EndedAt { get; set; }
}
