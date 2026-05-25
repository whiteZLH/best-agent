using BestAgent.Application.Abstractions;
using BestAgent.Domain.Agents;
using BestAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Repositories;

internal sealed class AgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentDefinitionRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AgentDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        return _dbContext.AgentDefinitions
            .Where(entity => entity.Code == code && entity.Enabled)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
