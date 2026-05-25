using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentDefinitions;

public record class AgentDefinitionVersion : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string AgentDefinitionId { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Instruction { get; init; }
    public string? SystemPromptTemplate { get; init; }
    public string DefaultModel { get; init; } = string.Empty;
    public string? AllowedTools { get; init; }
    public string? KnowledgeSources { get; init; }
    public string? MemoryPolicy { get; init; }
    public string? RoutingPolicy { get; init; }
    public string? ApprovalPolicy { get; init; }
    public string? ExecutionPolicy { get; init; }
    public string? PlannerPolicy { get; init; }
    public string? ContextPolicy { get; init; }
    public string? AllowedHandoffs { get; init; }
    public string? OutputSchema { get; init; }
    public int MaxTurns { get; init; }
    public decimal MaxCost { get; init; }
    public DateTime? PublishedAt { get; init; }
}
