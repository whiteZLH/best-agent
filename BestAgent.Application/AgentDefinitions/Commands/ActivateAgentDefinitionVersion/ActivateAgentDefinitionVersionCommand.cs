using MediatR;

namespace BestAgent.Application.AgentDefinitions.Commands.ActivateAgentDefinitionVersion;

public record ActivateAgentDefinitionVersionCommand(
    string AgentCode,
    int Version) : IRequest<AgentDefinitionViewModel>;
