namespace BestAgent.Api.Contracts.AgentDefinitions;

public record CreateAgentDefinitionRequest(
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
    IReadOnlyList<string>? DeniedTools = null);
