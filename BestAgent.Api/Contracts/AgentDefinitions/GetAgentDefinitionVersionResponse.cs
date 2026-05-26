namespace BestAgent.Api.Contracts.AgentDefinitions;

public record GetAgentDefinitionVersionResponse(
    string VersionId,
    int Version,
    string Status,
    string Name,
    string? Description,
    string? Instruction,
    string? SystemPromptTemplate,
    string DefaultModel,
    IReadOnlyList<string> AllowedTools,
    int MaxTurns,
    decimal MaxCost,
    bool IsCurrentVersion,
    DateTime CreateTime,
    DateTime LastModifyTime,
    DateTime? PublishedAt);
