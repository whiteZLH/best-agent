namespace BestAgent.Api.Contracts.AgentRuns;

public record StreamAgentRunEventResponse(
    string? EventId,
    string RunId,
    long? SeqNo,
    string EventType,
    string? RunStatus,
    DateTime? OccurredAt,
    EventDataInfoResponse Data);
