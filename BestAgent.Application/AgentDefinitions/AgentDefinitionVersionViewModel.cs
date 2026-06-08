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
    IReadOnlyList<string> KnowledgeSources,
    string? MemoryPolicy,
    string? RoutingPolicy,
    string? ApprovalPolicy,
    string? ExecutionPolicy,
    string? PlannerPolicy,
    string? ContextPolicy,
    IReadOnlyList<string> AllowedHandoffs,
    string? OutputSchema,
    int MaxTurns,
    decimal MaxCost,
    bool IsCurrentVersion,
    DateTime CreateTime,
    DateTime LastModifyTime,
    DateTime? PublishedAt,
    IReadOnlyList<string>? DeniedTools = null)
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
            AgentDefinitionToolListSerializer.Parse(version.KnowledgeSources),
            version.MemoryPolicy,
            version.RoutingPolicy,
            version.ApprovalPolicy,
            version.ExecutionPolicy,
            version.PlannerPolicy,
            version.ContextPolicy,
            AgentDefinitionToolListSerializer.Parse(version.AllowedHandoffs),
            version.OutputSchema,
            version.MaxTurns,
            version.MaxCost,
            version.Version == currentVersion,
            version.CreateTime,
            version.LastModifyTime,
            version.PublishedAt,
            AgentDefinitionToolListSerializer.Parse(version.DeniedTools));
    }
}
