using BestAgent.Domain.AgentDefinitions;

namespace BestAgent.Application.AgentDefinitions;

public record RouteRuleViewModel(
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
    DateTime LastModifyTime)
{
    public static RouteRuleViewModel FromRouteRule(RouteRule routeRule)
    {
        return new RouteRuleViewModel(
            routeRule.Id,
            routeRule.AgentDefinitionVersionId,
            routeRule.SourceAgentCode,
            routeRule.TargetAgentCode,
            routeRule.RuleName,
            routeRule.Priority,
            routeRule.MatchType,
            routeRule.MatchExpression,
            routeRule.HandoffMode,
            routeRule.MergeStrategy,
            routeRule.ContextScope,
            routeRule.MemoryScope,
            routeRule.ToolScope,
            routeRule.KnowledgeScope,
            routeRule.ApprovalRequired,
            routeRule.Enabled,
            routeRule.CreateTime,
            routeRule.LastModifyTime);
    }
}
