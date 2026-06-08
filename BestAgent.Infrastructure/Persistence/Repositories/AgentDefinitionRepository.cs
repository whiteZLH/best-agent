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

        return await ResolveDefinitionAsync(definition, cancellationToken);
    }

    public async Task<ResolvedAgentDefinition?> GetByCodeAsync(string agentCode, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.AgentDefinitions
            .AsNoTracking()
            .Where(x => x.Code == agentCode && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);

        if (definition is null)
        {
            return null;
        }

        return await ResolveDefinitionAsync(definition, cancellationToken);
    }

    public async Task<ResolvedAgentDefinition?> GetByVersionIdAsync(string versionId, CancellationToken cancellationToken)
    {
        var version = await _dbContext.AgentDefinitionVersions
            .AsNoTracking()
            .Where(x => x.Id == versionId && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return null;
        }

        var definition = await _dbContext.AgentDefinitions
            .AsNoTracking()
            .Where(x => x.Id == version.AgentDefinitionId && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);

        return definition is null ? null : new ResolvedAgentDefinition(definition, version);
    }

    public async Task<IReadOnlyList<ResolvedAgentDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        var definitions = await _dbContext.AgentDefinitions
            .AsNoTracking()
            .Where(x => !x.Deleted)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        if (definitions.Count == 0)
        {
            return Array.Empty<ResolvedAgentDefinition>();
        }

        var definitionIds = definitions.Select(x => x.Id).ToArray();
        var versions = await _dbContext.AgentDefinitionVersions
            .AsNoTracking()
            .Where(x => definitionIds.Contains(x.AgentDefinitionId) && !x.Deleted)
            .ToListAsync(cancellationToken);

        var versionLookup = versions.ToLookup(x => (x.AgentDefinitionId, x.Version));

        return definitions
            .Select(definition =>
            {
                var version = versionLookup[(definition.Id, definition.CurrentVersion)].SingleOrDefault();
                return version is null ? null : new ResolvedAgentDefinition(definition, version);
            })
            .Where(x => x is not null)
            .Cast<ResolvedAgentDefinition>()
            .ToArray();
    }

    public async Task<IReadOnlyList<AgentDefinitionVersion>> GetVersionsAsync(string agentCode, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.AgentDefinitions
            .AsNoTracking()
            .Where(x => x.Code == agentCode && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);

        if (definition is null)
        {
            return Array.Empty<AgentDefinitionVersion>();
        }

        return await _dbContext.AgentDefinitionVersions
            .AsNoTracking()
            .Where(x => x.AgentDefinitionId == definition.Id && !x.Deleted)
            .OrderByDescending(x => x.Version)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<AgentDefinitionVersion?> GetVersionByCodeAsync(string agentCode, int version, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.AgentDefinitions
            .AsNoTracking()
            .Where(x => x.Code == agentCode && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);

        if (definition is null)
        {
            return null;
        }

        return await _dbContext.AgentDefinitionVersions
            .AsNoTracking()
            .Where(x => x.AgentDefinitionId == definition.Id && x.Version == version && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        return _dbContext.AgentDefinitions.AnyAsync(cancellationToken);
    }

    public Task<bool> ExistsByCodeAsync(string agentCode, CancellationToken cancellationToken)
    {
        return _dbContext.AgentDefinitions.AnyAsync(x => x.Code == agentCode && !x.Deleted, cancellationToken);
    }

    public async Task AddAsync(ResolvedAgentDefinition definition, CancellationToken cancellationToken)
    {
        await _dbContext.AgentDefinitions.AddAsync(definition.Definition, cancellationToken);
        await _dbContext.AgentDefinitionVersions.AddAsync(definition.Version, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddVersionAsync(AgentDefinitionVersion version, CancellationToken cancellationToken)
    {
        await _dbContext.AgentDefinitionVersions.AddAsync(version, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ActivateVersionAsync(
        AgentDefinition definition,
        AgentDefinitionVersion targetVersion,
        AgentDefinitionVersion? previousVersion,
        CancellationToken cancellationToken)
    {
        _dbContext.AgentDefinitions.Update(definition);
        _dbContext.AgentDefinitionVersions.Update(targetVersion);

        if (previousVersion is not null)
        {
            _dbContext.AgentDefinitionVersions.Update(previousVersion);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ResolvedAgentDefinition?> ResolveDefinitionAsync(AgentDefinition definition, CancellationToken cancellationToken)
    {
        var version = await _dbContext.AgentDefinitionVersions
            .AsNoTracking()
            .Where(x =>
                x.AgentDefinitionId == definition.Id &&
                x.Version == definition.CurrentVersion &&
                !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);

        return version is null ? null : new ResolvedAgentDefinition(definition, version);
    }
}
