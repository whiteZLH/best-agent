namespace BestAgent.Api.Contracts.AgentDefinitions;

public record GetAgentDefinitionResponse(
    string Code,
    string Name,
    string? Description,
    bool Enabled,
    int CurrentVersion,
    string VersionId,
    int Version,
    string VersionStatus,
    string VersionName,
    string? VersionDescription,
    string? Instruction,
    string? SystemPromptTemplate,
    string DefaultModel,
    IReadOnlyList<string> AllowedTools,
    int MaxTurns,
    decimal MaxCost,
    DateTime CreateTime,
    DateTime LastModifyTime,
    DateTime? PublishedAt);
