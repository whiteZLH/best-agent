using MediatR;

namespace BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinitionVersion;

public record CreateAgentDefinitionVersionCommand(
    string AgentCode,
    string? Name,
    string? Description,
    string? Instruction,
    string SystemPromptTemplate,
    string DefaultModel,
    IReadOnlyList<string>? AllowedTools,
    int MaxTurns,
    decimal MaxCost) : IRequest<AgentDefinitionVersionViewModel>;
