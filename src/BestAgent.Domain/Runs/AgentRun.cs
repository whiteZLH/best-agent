using BestAgent.Domain.Common;

namespace BestAgent.Domain.Runs;

public sealed class AgentRun : AuditedEntity
{
    public string RunId { get; set; } = string.Empty;

    public string AgentCode { get; set; } = string.Empty;

    public AgentRunStatus Status { get; set; } = AgentRunStatus.Created;

    public string InputPayload { get; set; } = string.Empty;

    public string? OutputPayload { get; set; }

    public int CurrentStepNo { get; set; }

    public int StatusVersion { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public void MoveToRunning(DateTimeOffset now)
    {
        Status = AgentRunStatus.Running;
        StartedAt = StartedAt == default ? now : StartedAt;
        StatusVersion++;
        ErrorMessage = null;
    }

    public void Complete(string outputPayload, DateTimeOffset now)
    {
        Status = AgentRunStatus.Completed;
        OutputPayload = outputPayload;
        EndedAt = now;
        StatusVersion++;
        ErrorMessage = null;
    }

    public void Fail(string errorMessage, DateTimeOffset now)
    {
        Status = AgentRunStatus.Failed;
        ErrorMessage = errorMessage;
        EndedAt = now;
        StatusVersion++;
    }

    public void IncrementStep()
    {
        CurrentStepNo++;
    }

    public bool IsTerminal()
    {
        return Status is AgentRunStatus.Completed or AgentRunStatus.Failed;
    }
}
