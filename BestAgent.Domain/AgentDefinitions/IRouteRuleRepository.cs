namespace BestAgent.Domain.AgentDefinitions;

public interface IRouteRuleRepository
{
    Task<IReadOnlyList<RouteRule>> GetByAgentDefinitionVersionIdAsync(string agentDefinitionVersionId, CancellationToken cancellationToken);

    Task<bool> ExistsByVersionIdAndRuleNameAsync(
        string agentDefinitionVersionId,
        string ruleName,
        CancellationToken cancellationToken);

    Task AddAsync(RouteRule routeRule, CancellationToken cancellationToken);
}
