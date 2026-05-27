using BestAgent.Domain.Tools;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class ToolDefinitionRepository : IToolDefinitionRepository
{
    private readonly BestAgentDbContext _dbContext;

    public ToolDefinitionRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ToolDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        return await _dbContext.ToolDefinitions
            .AsNoTracking()
            .Where(x => x.Id == id && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ToolDefinition?> GetByToolNameAsync(string toolName, CancellationToken cancellationToken)
    {
        return await _dbContext.ToolDefinitions
            .AsNoTracking()
            .Where(x => x.ToolName == toolName && !x.Deleted)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.ToolDefinitions
            .AsNoTracking()
            .Where(x => !x.Deleted)
            .OrderBy(x => x.ToolName)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.ToolDefinitions
            .AsNoTracking()
            .Where(x => x.Enabled && !x.Deleted)
            .OrderBy(x => x.ToolName)
            .ToArrayAsync(cancellationToken);
    }

    public Task<bool> ExistsByToolNameAsync(string toolName, CancellationToken cancellationToken)
    {
        return _dbContext.ToolDefinitions.AnyAsync(x => x.ToolName == toolName && !x.Deleted, cancellationToken);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        return _dbContext.ToolDefinitions.AnyAsync(cancellationToken);
    }

    public async Task AddAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
    {
        await _dbContext.ToolDefinitions.AddAsync(toolDefinition, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
    {
        _dbContext.ToolDefinitions.Update(toolDefinition);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var deleted = toolDefinition with
        {
            Deleted = true,
            LastModifier = "system",
            LastModifierName = "system",
            LastModifyTime = now
        };

        _dbContext.ToolDefinitions.Update(deleted);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
