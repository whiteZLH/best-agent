using BestAgent.Domain.Messages;

namespace BestAgent.Application.Abstractions;

public interface IAgentMessageRepository
{
    Task AddAsync(AgentMessage message, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentMessage>> ListByRunIdAsync(string runId, CancellationToken cancellationToken);
}
