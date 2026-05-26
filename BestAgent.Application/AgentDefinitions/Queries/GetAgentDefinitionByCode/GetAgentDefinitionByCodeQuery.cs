using MediatR;

namespace BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitionByCode;

public record GetAgentDefinitionByCodeQuery(string AgentCode) : IRequest<AgentDefinitionViewModel?>;
