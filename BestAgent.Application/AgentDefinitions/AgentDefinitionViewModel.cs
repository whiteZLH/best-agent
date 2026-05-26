namespace BestAgent.Application.AgentDefinitions;

public record AgentDefinitionViewModel(
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
    DateTime? PublishedAt)
{
    public static AgentDefinitionViewModel FromResolvedDefinition(Domain.AgentDefinitions.ResolvedAgentDefinition definition)
    {
        return new AgentDefinitionViewModel(
            definition.Definition.Code,
            definition.Definition.Name,
            definition.Definition.Description,
            definition.Definition.Enabled,
            definition.Definition.CurrentVersion,
            definition.Version.Id,
            definition.Version.Version,
            definition.Version.Status,
            definition.Version.Name,
            definition.Version.Description,
            definition.Version.Instruction,
            definition.Version.SystemPromptTemplate,
            definition.Version.DefaultModel,
            AgentDefinitionToolListSerializer.Parse(definition.Version.AllowedTools),
            definition.Version.MaxTurns,
            definition.Version.MaxCost,
            definition.Definition.CreateTime,
            definition.Definition.LastModifyTime,
            definition.Version.PublishedAt);
    }
}
