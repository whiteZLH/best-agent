namespace BestAgent.Domain.AgentDefinitions;

public interface IAgentDefinitionRepository
{
    Task<ResolvedAgentDefinition?> GetEnabledByCodeAsync(string agentCode, CancellationToken cancellationToken);

    Task<ResolvedAgentDefinition?> GetByCodeAsync(string agentCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResolvedAgentDefinition>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentDefinitionVersion>> GetVersionsAsync(string agentCode, CancellationToken cancellationToken);

    Task<AgentDefinitionVersion?> GetVersionByCodeAsync(string agentCode, int version, CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    Task<bool> ExistsByCodeAsync(string agentCode, CancellationToken cancellationToken);

    Task AddAsync(ResolvedAgentDefinition definition, CancellationToken cancellationToken);

    Task AddVersionAsync(AgentDefinitionVersion version, CancellationToken cancellationToken);

    Task ActivateVersionAsync(
        AgentDefinition definition,
        AgentDefinitionVersion targetVersion,
        AgentDefinitionVersion? previousVersion,
        CancellationToken cancellationToken);
}
