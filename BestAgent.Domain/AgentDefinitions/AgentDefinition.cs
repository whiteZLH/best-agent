using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentDefinitions;

public record class AgentDefinition : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Enabled { get; init; }
    public int CurrentVersion { get; init; }
}
