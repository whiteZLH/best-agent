using BestAgent.Domain.AgentDefinitions;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class AgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentDefinitionRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ResolvedAgentDefinition?> GetEnabledByCodeAsync(string agentCode, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.AgentDefinitions
            .AsNoTracking()
            .Where(x => x.Code == agentCode && x.Enabled && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);

        if (definition is null)
        {
            return null;
        }

        var version = await _dbContext.AgentDefinitionVersions
            .AsNoTracking()
            .Where(x =>
                x.AgentDefinitionId == definition.Id &&
                x.Version == definition.CurrentVersion &&
                !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);

        return version is null ? null : new ResolvedAgentDefinition(definition, version);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        return _dbContext.AgentDefinitions.AnyAsync(cancellationToken);
    }

    public async Task AddAsync(ResolvedAgentDefinition definition, CancellationToken cancellationToken)
    {
        await _dbContext.AgentDefinitions.AddAsync(definition.Definition, cancellationToken);
        await _dbContext.AgentDefinitionVersions.AddAsync(definition.Version, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
