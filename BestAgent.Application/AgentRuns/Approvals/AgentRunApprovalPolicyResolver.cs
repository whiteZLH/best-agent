using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Approvals;

public static class AgentRunApprovalPolicyResolver
{
    public static async Task<ApprovalPolicyOptions?> ResolveEffectivePolicyAsync(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        AgentRun agentRun,
        ApprovalPolicyOptions? globalApprovalPolicyOptions,
        TenantApprovalPolicyOptions? tenantApprovalPolicyOptions,
        CancellationToken cancellationToken)
    {
        var resolvedDefinition = await ResolveDefinitionForRunAsync(
            agentDefinitionRepository,
            agentRunRepository,
            agentStepRepository,
            agentRun,
            cancellationToken);

        return ResolveEffectivePolicy(
            agentRun,
            resolvedDefinition?.Version.ApprovalPolicy,
            globalApprovalPolicyOptions,
            tenantApprovalPolicyOptions);
    }

    public static ApprovalPolicyOptions ResolveEffectivePolicy(
        AgentRun agentRun,
        string? versionApprovalPolicy,
        ApprovalPolicyOptions? globalApprovalPolicyOptions,
        TenantApprovalPolicyOptions? tenantApprovalPolicyOptions)
    {
        var normalizedGlobalPolicy = ApprovalPolicyOptionsNormalizer.Normalize(globalApprovalPolicyOptions);
        var tenantPolicy = ResolveTenantPolicy(agentRun.TenantId, tenantApprovalPolicyOptions);
        var versionPolicy = ApprovalPolicyParser.ParseOptional(versionApprovalPolicy);

        if (tenantPolicy is null)
        {
            return ApprovalPolicyParser.Merge(normalizedGlobalPolicy, versionPolicy);
        }

        var tenantResolvedPolicy = ApprovalPolicyParser.Merge(normalizedGlobalPolicy, tenantPolicy);
        if (versionPolicy is null)
        {
            return tenantResolvedPolicy;
        }

        return MergeTenantBoundary(tenantResolvedPolicy, tenantPolicy, versionPolicy);
    }

    private static async Task<ResolvedAgentDefinition?> ResolveDefinitionForRunAsync(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        AgentRun agentRun,
        CancellationToken cancellationToken)
    {
        ResolvedAgentDefinition? resolvedDefinition;
        if (!string.IsNullOrWhiteSpace(agentRun.AgentDefinitionVersionId))
        {
            var boundDefinition = await agentDefinitionRepository.GetByVersionIdAsync(
                agentRun.AgentDefinitionVersionId,
                cancellationToken);
            if (boundDefinition is not null)
            {
                resolvedDefinition = boundDefinition;
                return await ApplyParentApprovalBoundaryIfNeeded(
                    agentDefinitionRepository,
                    agentRunRepository,
                    agentStepRepository,
                    agentRun,
                    resolvedDefinition,
                    cancellationToken);
            }
        }

        resolvedDefinition = await agentDefinitionRepository.GetEnabledByCodeAsync(agentRun.AgentCode, cancellationToken);
        return await ApplyParentApprovalBoundaryIfNeeded(
            agentDefinitionRepository,
            agentRunRepository,
            agentStepRepository,
            agentRun,
            resolvedDefinition,
            cancellationToken);
    }

    private static async Task<ResolvedAgentDefinition?> ApplyParentApprovalBoundaryIfNeeded(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        AgentRun agentRun,
        ResolvedAgentDefinition? resolvedDefinition,
        CancellationToken cancellationToken)
    {
        if (resolvedDefinition is null
            || string.IsNullOrWhiteSpace(agentRun.ParentRunId))
        {
            return resolvedDefinition;
        }

        var parentStep = await agentStepRepository.GetLastByRunIdAsync(agentRun.ParentRunId, cancellationToken);
        if (parentStep is null
            || !string.Equals(parentStep.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
            || !HandoffPayloadSerializer.TryParse(parentStep.DecisionPayload, out var handoffPayload)
            || !string.Equals(handoffPayload!.ChildRunId, agentRun.RunId, StringComparison.Ordinal))
        {
            return resolvedDefinition;
        }

        var parentRun = await agentRunRepository.GetByRunIdAsync(agentRun.ParentRunId, cancellationToken);
        if (parentRun is null)
        {
            return resolvedDefinition;
        }

        var parentResolvedDefinition = await ResolveDefinitionForRunAsync(
            agentDefinitionRepository,
            agentRunRepository,
            agentStepRepository,
            parentRun,
            cancellationToken);
        if (parentResolvedDefinition is null)
        {
            return resolvedDefinition;
        }

        var restrictedApprovalPolicy = ResolveRestrictedApprovalPolicy(
            resolvedDefinition.Version.ApprovalPolicy,
            parentResolvedDefinition.Version.ApprovalPolicy);
        if (restrictedApprovalPolicy is null)
        {
            return resolvedDefinition;
        }

        return resolvedDefinition with
        {
            Version = resolvedDefinition.Version with
            {
                ApprovalPolicy = restrictedApprovalPolicy
            }
        };
    }

    private static string? ResolveRestrictedApprovalPolicy(string? currentApprovalPolicy, string? parentApprovalPolicy)
    {
        var currentPolicy = ApprovalPolicyParser.ParseOptional(currentApprovalPolicy);
        var parentPolicy = ApprovalPolicyParser.ParseOptional(parentApprovalPolicy);
        if (currentPolicy is null && parentPolicy is null)
        {
            return null;
        }

        var merged = ApprovalPolicyInheritance.MergeStricter(parentPolicy, currentPolicy);
        return JsonSerializer.Serialize(merged);
    }

    private static ApprovalPolicyOptions? ResolveTenantPolicy(
        string? tenantId,
        TenantApprovalPolicyOptions? tenantApprovalPolicyOptions)
    {
        var normalizedTenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTenantId)
            || tenantApprovalPolicyOptions?.PoliciesByTenantId is null
            || tenantApprovalPolicyOptions.PoliciesByTenantId.Count == 0
            || !tenantApprovalPolicyOptions.PoliciesByTenantId.TryGetValue(normalizedTenantId, out var policy))
        {
            return null;
        }

        return ApprovalPolicyOptionsNormalizer.Normalize(policy);
    }

    private static ApprovalPolicyOptions MergeTenantBoundary(
        ApprovalPolicyOptions tenantResolvedPolicy,
        ApprovalPolicyOptions tenantPolicy,
        ApprovalPolicyOptions versionPolicy)
    {
        var normalizedTenantResolvedPolicy = ApprovalPolicyOptionsNormalizer.Normalize(tenantResolvedPolicy);
        var normalizedTenantPolicy = ApprovalPolicyOptionsNormalizer.Normalize(tenantPolicy);
        var normalizedVersionPolicy = ApprovalPolicyOptionsNormalizer.Normalize(versionPolicy);

        var effectiveAllowedRoles = normalizedTenantPolicy.AllowedApproverRoles.Count > 0
            ? normalizedVersionPolicy.AllowedApproverRoles.Count > 0
                ? normalizedTenantResolvedPolicy.AllowedApproverRoles
                    .Where(role => normalizedVersionPolicy.AllowedApproverRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : normalizedTenantResolvedPolicy.AllowedApproverRoles.ToArray()
            : normalizedVersionPolicy.AllowedApproverRoles.Count > 0
                ? normalizedVersionPolicy.AllowedApproverRoles.ToArray()
                : normalizedTenantResolvedPolicy.AllowedApproverRoles.ToArray();

        return ApprovalPolicyOptionsNormalizer.Normalize(new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = normalizedTenantResolvedPolicy.ApprovalRequiredSideEffectLevels
                .Concat(normalizedVersionPolicy.ApprovalRequiredSideEffectLevels)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RoleRequiredSideEffectLevels = normalizedTenantResolvedPolicy.RoleRequiredSideEffectLevels
                .Concat(normalizedVersionPolicy.RoleRequiredSideEffectLevels)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AllowedApproverRoles = effectiveAllowedRoles,
            ParameterApprovalRules = normalizedTenantResolvedPolicy.ParameterApprovalRules
                .Concat(normalizedVersionPolicy.ParameterApprovalRules)
                .GroupBy(
                    rule => $"{rule.ToolName}|{rule.InputPath}|{rule.ExpectedValue}|{rule.OverrideSideEffectLevel}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray()
        });
    }
}
