using BestAgent.Domain.AgentDefinitions;
using MediatR;

namespace BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitionVersions;

public class GetAgentDefinitionVersionsQueryHandler
    : IRequestHandler<GetAgentDefinitionVersionsQuery, IReadOnlyList<AgentDefinitionVersionViewModel>>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;

    public GetAgentDefinitionVersionsQueryHandler(IAgentDefinitionRepository agentDefinitionRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
    }

    public async Task<IReadOnlyList<AgentDefinitionVersionViewModel>> Handle(
        GetAgentDefinitionVersionsQuery request,
        CancellationToken cancellationToken)
    {
        var definition = await _agentDefinitionRepository.GetByCodeAsync(request.AgentCode, cancellationToken);
        if (definition is null)
        {
            return Array.Empty<AgentDefinitionVersionViewModel>();
        }

        var versions = await _agentDefinitionRepository.GetVersionsAsync(request.AgentCode, cancellationToken);
        return versions
            .Select(x => AgentDefinitionVersionViewModel.FromVersion(x, definition.Definition.CurrentVersion))
            .ToArray();
    }
}
