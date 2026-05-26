namespace BestAgent.Api.Contracts.AgentDefinitions;

public record CreateAgentDefinitionVersionRequest(
    string? Name,
    string? Description,
    string? Instruction,
    string SystemPromptTemplate,
    string DefaultModel,
    IReadOnlyList<string>? AllowedTools,
    int MaxTurns,
    decimal MaxCost);
