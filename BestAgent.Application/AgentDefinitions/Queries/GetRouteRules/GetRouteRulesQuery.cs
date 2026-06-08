using MediatR;

namespace BestAgent.Application.AgentDefinitions.Queries.GetRouteRules;

public record GetRouteRulesQuery(string AgentCode, int Version) : IRequest<IReadOnlyList<RouteRuleViewModel>>;
