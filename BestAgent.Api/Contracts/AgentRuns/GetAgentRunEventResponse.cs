namespace BestAgent.Api.Contracts.AgentRuns;

public record GetAgentRunEventResponse(
    string EventId,
    string RunId,
    long SeqNo,
    string EventType,
    string RunStatus,
    string? Payload,
    EventDataInfoResponse? Data,
    string PublishStatus,
    DateTime? PublishedAt,
    int RetryCount,
    DateTime OccurredAt,
    DateTime CreateTime,
    DateTime LastModifyTime);
