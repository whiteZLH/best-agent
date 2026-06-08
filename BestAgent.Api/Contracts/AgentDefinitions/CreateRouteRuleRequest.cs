namespace BestAgent.Api.Contracts.AgentDefinitions;

public record CreateRouteRuleRequest(
    string TargetAgentCode,
    string RuleName,
    int Priority,
    string MatchType,
    string? MatchExpression,
    string HandoffMode,
    string? MergeStrategy,
    string? ContextScope,
    string? MemoryScope,
    string? ToolScope,
    string? KnowledgeScope,
    bool ApprovalRequired,
    bool Enabled);
