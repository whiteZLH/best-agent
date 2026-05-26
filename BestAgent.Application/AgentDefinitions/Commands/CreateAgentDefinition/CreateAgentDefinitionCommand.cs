using MediatR;

namespace BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinition;

public record CreateAgentDefinitionCommand(
    string Code,
    string Name,
    string? Description,
    string? Instruction,
    string SystemPromptTemplate,
    string DefaultModel,
    IReadOnlyList<string>? AllowedTools,
    int MaxTurns,
    decimal MaxCost,
    bool Enabled) : IRequest<AgentDefinitionViewModel>;
