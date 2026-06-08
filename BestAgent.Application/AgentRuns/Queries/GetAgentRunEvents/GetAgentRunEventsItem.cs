using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;

public record GetAgentRunEventsItem(
    string EventId,
    string RunId,
    long SeqNo,
    string EventType,
    string RunStatus,
    string? Payload,
    EventDataInfo? Data,
    string PublishStatus,
    DateTime? PublishedAt,
    int RetryCount,
    DateTime OccurredAt,
    DateTime CreateTime,
    DateTime LastModifyTime)
{
    public static GetAgentRunEventsItem FromEntity(RunOutboxEvent entity)
    {
        var maskedPayload = RuntimeEventPayloadMasker.MaskPayload(entity.Payload);
        return new GetAgentRunEventsItem(
            entity.EventId,
            entity.RunId,
            entity.SeqNo,
            entity.EventType,
            entity.RunStatus,
            maskedPayload,
            EventDataInfo.FromPayload(maskedPayload),
            entity.PublishStatus,
            entity.PublishedAt,
            entity.RetryCount,
            entity.OccurredAt,
            entity.CreateTime,
            entity.LastModifyTime);
    }
}
