using BestAgent.Domain.AgentDefinitions;
using MediatR;

namespace BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitions;

public class GetAgentDefinitionsQueryHandler : IRequestHandler<GetAgentDefinitionsQuery, IReadOnlyList<AgentDefinitionViewModel>>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;

    public GetAgentDefinitionsQueryHandler(IAgentDefinitionRepository agentDefinitionRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
    }

    public async Task<IReadOnlyList<AgentDefinitionViewModel>> Handle(GetAgentDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var definitions = await _agentDefinitionRepository.GetAllAsync(cancellationToken);
        return definitions
            .Select(AgentDefinitionViewModel.FromResolvedDefinition)
            .ToArray();
    }
}
