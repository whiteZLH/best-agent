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
    IReadOnlyList<string>? KnowledgeSources,
    string? MemoryPolicy,
    string? RoutingPolicy,
    string? ApprovalPolicy,
    string? ExecutionPolicy,
    string? PlannerPolicy,
    string? ContextPolicy,
    IReadOnlyList<string>? AllowedHandoffs,
    string? OutputSchema,
    int MaxTurns,
    decimal MaxCost,
    bool Enabled,
    IReadOnlyList<string>? DeniedTools = null) : IRequest<AgentDefinitionViewModel>;
