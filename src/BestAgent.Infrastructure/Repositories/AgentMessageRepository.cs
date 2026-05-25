using BestAgent.Application.Abstractions;
using BestAgent.Domain.Messages;
using BestAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Repositories;

internal sealed class AgentMessageRepository : IAgentMessageRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentMessageRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(AgentMessage message, CancellationToken cancellationToken)
    {
        return _dbContext.AgentMessages.AddAsync(message, cancellationToken).AsTask();
    }

    public async Task<IReadOnlyList<AgentMessage>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return await _dbContext.AgentMessages
            .Where(entity => entity.RunId == runId)
            .OrderBy(entity => entity.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
