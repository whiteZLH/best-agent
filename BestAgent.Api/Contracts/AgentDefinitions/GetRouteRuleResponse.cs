namespace BestAgent.Api.Contracts.AgentDefinitions;

public record GetRouteRuleResponse(
    string Id,
    string AgentDefinitionVersionId,
    string SourceAgentCode,
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
    bool Enabled,
    DateTime CreateTime,
    DateTime LastModifyTime);
