using BestAgent.Application.Exceptions;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentDefinitions;
using MediatR;

namespace BestAgent.Application.AgentDefinitions.Commands.CreateRouteRule;

public class CreateRouteRuleCommandHandler : IRequestHandler<CreateRouteRuleCommand, RouteRuleViewModel>
{
    private static readonly HashSet<string> SupportedHandoffModes =
    [
        "route_only",
        "delegate_and_wait",
        "delegate_and_merge"
    ];

    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IRouteRuleRepository _routeRuleRepository;

    public CreateRouteRuleCommandHandler(
        IAgentDefinitionRepository agentDefinitionRepository,
        IRouteRuleRepository routeRuleRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
        _routeRuleRepository = routeRuleRepository;
    }

    public async Task<RouteRuleViewModel> Handle(CreateRouteRuleCommand request, CancellationToken cancellationToken)
    {
        var agentCode = request.AgentCode.Trim();
        var targetAgentCode = request.TargetAgentCode.Trim();
        var ruleName = request.RuleName.Trim();
        var matchType = request.MatchType.Trim();
        var handoffMode = request.HandoffMode.Trim().ToLowerInvariant();
        var normalizedMergeStrategy = HandoffPayloadSerializer.NormalizeMergeStrategy(handoffMode, request.MergeStrategy);
        var normalizedMatchExpression = AgentDefinitionJsonPolicySerializer.NormalizeOptionalJson(request.MatchExpression, "Match expression");
        var normalizedContextScope = AgentDefinitionJsonPolicySerializer.NormalizeOptionalJson(request.ContextScope, "Context scope");
        var normalizedMemoryScope = AgentDefinitionJsonPolicySerializer.NormalizeOptionalJson(request.MemoryScope, "Memory scope");
        var normalizedToolScope = AgentDefinitionJsonPolicySerializer.NormalizeOptionalJson(request.ToolScope, "Tool scope");
        var normalizedKnowledgeScope = AgentDefinitionJsonPolicySerializer.NormalizeOptionalJson(request.KnowledgeScope, "Knowledge scope");

        if (string.IsNullOrWhiteSpace(agentCode))
        {
            throw new InvalidOperationException("Agent code is required.");
        }

        if (request.Version <= 0)
        {
            throw new InvalidOperationException("Version must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(targetAgentCode))
        {
            throw new InvalidOperationException("Target agent code is required.");
        }

        if (string.IsNullOrWhiteSpace(ruleName))
        {
            throw new InvalidOperationException("Route rule name is required.");
        }

        if (string.IsNullOrWhiteSpace(matchType))
        {
            throw new InvalidOperationException("Match type is required.");
        }

        if (string.IsNullOrWhiteSpace(handoffMode))
        {
            throw new InvalidOperationException("Handoff mode is required.");
        }

        if (!SupportedHandoffModes.Contains(handoffMode))
        {
            throw new InvalidOperationException(
                $"Handoff mode '{handoffMode}' is not supported. Supported values: route_only, delegate_and_wait, delegate_and_merge.");
        }

        var version = await _agentDefinitionRepository.GetVersionByCodeAsync(agentCode, request.Version, cancellationToken);
        if (version is null)
        {
            throw new NotFoundException("AgentDefinitionVersion", $"{agentCode}:{request.Version}");
        }

        if (await _routeRuleRepository.ExistsByVersionIdAndRuleNameAsync(version.Id, ruleName, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Route rule '{ruleName}' already exists for agent '{agentCode}' version '{request.Version}'.");
        }

        var now = DateTime.UtcNow;
        var routeRule = new RouteRule
        {
            Id = Guid.NewGuid().ToString("N"),
            AgentDefinitionVersionId = version.Id,
            SourceAgentCode = agentCode,
            TargetAgentCode = targetAgentCode,
            RuleName = ruleName,
            Priority = request.Priority,
            MatchType = matchType,
            MatchExpression = normalizedMatchExpression,
            HandoffMode = handoffMode,
            MergeStrategy = normalizedMergeStrategy,
            ContextScope = normalizedContextScope,
            MemoryScope = normalizedMemoryScope,
            ToolScope = normalizedToolScope,
            KnowledgeScope = normalizedKnowledgeScope,
            ApprovalRequired = request.ApprovalRequired,
            Enabled = request.Enabled,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        await _routeRuleRepository.AddAsync(routeRule, cancellationToken);
        return RouteRuleViewModel.FromRouteRule(routeRule);
    }
}
