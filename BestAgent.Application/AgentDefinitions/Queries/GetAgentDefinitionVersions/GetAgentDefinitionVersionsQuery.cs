using MediatR;

namespace BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitionVersions;

public record GetAgentDefinitionVersionsQuery(string AgentCode) : IRequest<IReadOnlyList<AgentDefinitionVersionViewModel>>;
