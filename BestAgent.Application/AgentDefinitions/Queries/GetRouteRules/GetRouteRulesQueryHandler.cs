using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentDefinitions;
using MediatR;

namespace BestAgent.Application.AgentDefinitions.Queries.GetRouteRules;

public class GetRouteRulesQueryHandler : IRequestHandler<GetRouteRulesQuery, IReadOnlyList<RouteRuleViewModel>>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IRouteRuleRepository _routeRuleRepository;

    public GetRouteRulesQueryHandler(
        IAgentDefinitionRepository agentDefinitionRepository,
        IRouteRuleRepository routeRuleRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
        _routeRuleRepository = routeRuleRepository;
    }

    public async Task<IReadOnlyList<RouteRuleViewModel>> Handle(GetRouteRulesQuery request, CancellationToken cancellationToken)
    {
        var agentCode = request.AgentCode.Trim();
        if (string.IsNullOrWhiteSpace(agentCode))
        {
            throw new InvalidOperationException("Agent code is required.");
        }

        if (request.Version <= 0)
        {
            throw new InvalidOperationException("Version must be greater than zero.");
        }

        var version = await _agentDefinitionRepository.GetVersionByCodeAsync(agentCode, request.Version, cancellationToken);
        if (version is null)
        {
            throw new NotFoundException("AgentDefinitionVersion", $"{agentCode}:{request.Version}");
        }

        var routeRules = await _routeRuleRepository.GetByAgentDefinitionVersionIdAsync(version.Id, cancellationToken);
        return routeRules
            .Select(RouteRuleViewModel.FromRouteRule)
            .ToArray();
    }
}
