using MediatR;

namespace BestAgent.Application.AgentDefinitions.Commands.CreateRouteRule;

public record CreateRouteRuleCommand(
    string AgentCode,
    int Version,
    string TargetAgentCode,
    string RuleName,
    int Priority,
    string MatchType,
    string? MatchExpression,
    string HandoffMode,
    string? ContextScope,
    string? MemoryScope,
    string? ToolScope,
    string? KnowledgeScope,
    bool ApprovalRequired,
    bool Enabled) : IRequest<RouteRuleViewModel>;
