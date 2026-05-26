using MediatR;

namespace BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitions;

public record GetAgentDefinitionsQuery() : IRequest<IReadOnlyList<AgentDefinitionViewModel>>;
