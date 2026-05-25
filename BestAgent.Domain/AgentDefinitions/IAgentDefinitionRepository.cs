namespace BestAgent.Domain.AgentDefinitions;

public interface IAgentDefinitionRepository
{
    Task<ResolvedAgentDefinition?> GetEnabledByCodeAsync(string agentCode, CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    Task AddAsync(ResolvedAgentDefinition definition, CancellationToken cancellationToken);
}
