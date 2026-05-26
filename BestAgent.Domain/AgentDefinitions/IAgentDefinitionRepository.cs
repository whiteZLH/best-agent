namespace BestAgent.Domain.AgentDefinitions;

public interface IAgentDefinitionRepository
{
    Task<ResolvedAgentDefinition?> GetEnabledByCodeAsync(string agentCode, CancellationToken cancellationToken);

    Task<ResolvedAgentDefinition?> GetByCodeAsync(string agentCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResolvedAgentDefinition>> GetAllAsync(CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    Task<bool> ExistsByCodeAsync(string agentCode, CancellationToken cancellationToken);

    Task AddAsync(ResolvedAgentDefinition definition, CancellationToken cancellationToken);
}
