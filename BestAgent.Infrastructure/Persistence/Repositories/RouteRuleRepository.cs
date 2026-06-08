using BestAgent.Domain.AgentDefinitions;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class RouteRuleRepository : IRouteRuleRepository
{
    private readonly BestAgentDbContext _dbContext;

    public RouteRuleRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RouteRule>> GetByAgentDefinitionVersionIdAsync(
        string agentDefinitionVersionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.RouteRules
            .AsNoTracking()
            .Where(x => x.AgentDefinitionVersionId == agentDefinitionVersionId && !x.Deleted)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.RuleName)
            .ToArrayAsync(cancellationToken);
    }

    public Task<bool> ExistsByVersionIdAndRuleNameAsync(
        string agentDefinitionVersionId,
        string ruleName,
        CancellationToken cancellationToken)
    {
        return _dbContext.RouteRules.AnyAsync(
            x => x.AgentDefinitionVersionId == agentDefinitionVersionId
                && x.RuleName == ruleName
                && !x.Deleted,
            cancellationToken);
    }

    public async Task AddAsync(RouteRule routeRule, CancellationToken cancellationToken)
    {
        await _dbContext.RouteRules.AddAsync(routeRule, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
