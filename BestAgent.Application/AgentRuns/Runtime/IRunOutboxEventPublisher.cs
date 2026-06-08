using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Runtime;

public interface IRunOutboxEventPublisher
{
    Task PublishAsync(RunOutboxEvent outboxEvent, CancellationToken cancellationToken);
}
