using BestAgent.Domain.Agents;

namespace BestAgent.Application.Abstractions;

public interface IAgentDefinitionRepository
{
    Task<AgentDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken);
}
