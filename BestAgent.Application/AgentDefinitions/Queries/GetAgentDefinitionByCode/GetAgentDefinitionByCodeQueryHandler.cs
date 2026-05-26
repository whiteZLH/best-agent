using BestAgent.Domain.AgentDefinitions;
using MediatR;

namespace BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitionByCode;

public class GetAgentDefinitionByCodeQueryHandler : IRequestHandler<GetAgentDefinitionByCodeQuery, AgentDefinitionViewModel?>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;

    public GetAgentDefinitionByCodeQueryHandler(IAgentDefinitionRepository agentDefinitionRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
    }

    public async Task<AgentDefinitionViewModel?> Handle(GetAgentDefinitionByCodeQuery request, CancellationToken cancellationToken)
    {
        var definition = await _agentDefinitionRepository.GetByCodeAsync(request.AgentCode, cancellationToken);
        return definition is null ? null : AgentDefinitionViewModel.FromResolvedDefinition(definition);
    }
}
