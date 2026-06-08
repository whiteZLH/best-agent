namespace BestAgent.Application.AgentRuns.Runtime;

public record AgentRunEvent(
    string RunId,
    string EventType,
    AgentRunEventData Data,
    string? EventId = null,
    long? SeqNo = null,
    string? RunStatus = null,
    DateTime? OccurredAt = null);

public record AgentRunEventData(
    int StepNo,
    string StepType,
    string Status,
    string? Output = null,
    string? Error = null,
    string? ModelCall = null);
