using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentDefinitions;

public record class RouteRule : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string AgentDefinitionVersionId { get; init; } = string.Empty;
    public string SourceAgentCode { get; init; } = string.Empty;
    public string TargetAgentCode { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string MatchType { get; init; } = string.Empty;
    public string? MatchExpression { get; init; }
    public string HandoffMode { get; init; } = string.Empty;
    public string? ContextScope { get; init; }
    public string? ToolScope { get; init; }
    public string? KnowledgeScope { get; init; }
    public bool ApprovalRequired { get; init; }
    public bool Enabled { get; init; }
}
