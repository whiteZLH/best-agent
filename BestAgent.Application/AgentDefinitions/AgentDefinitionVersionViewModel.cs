using BestAgent.Domain.AgentDefinitions;

namespace BestAgent.Application.AgentDefinitions;

public record AgentDefinitionVersionViewModel(
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
    DateTime? PublishedAt)
{
    public static AgentDefinitionVersionViewModel FromVersion(AgentDefinitionVersion version, int currentVersion)
    {
        return new AgentDefinitionVersionViewModel(
            version.Id,
            version.Version,
            version.Status,
            version.Name,
            version.Description,
            version.Instruction,
            version.SystemPromptTemplate,
            version.DefaultModel,
            AgentDefinitionToolListSerializer.Parse(version.AllowedTools),
            version.MaxTurns,
            version.MaxCost,
            version.Version == currentVersion,
            version.CreateTime,
            version.LastModifyTime,
            version.PublishedAt);
    }
}
