using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;

namespace BestAgent.Infrastructure.Runtime;

public class EventBusRunOutboxEventPublisher : IRunOutboxEventPublisher
{
    private readonly IAgentRunEventBus _eventBus;

    public EventBusRunOutboxEventPublisher(IAgentRunEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public Task PublishAsync(RunOutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = DeserializeData(outboxEvent.Payload)
            ?? new AgentRunEventData(0, outboxEvent.EventType, outboxEvent.RunStatus);
        _eventBus.Publish(
            new AgentRunEvent(
                outboxEvent.RunId,
                outboxEvent.EventType,
                data,
                outboxEvent.EventId,
                outboxEvent.SeqNo,
                outboxEvent.RunStatus,
                outboxEvent.OccurredAt));

        return Task.CompletedTask;
    }

    private static AgentRunEventData? DeserializeData(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AgentRunEventData>(
                payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
