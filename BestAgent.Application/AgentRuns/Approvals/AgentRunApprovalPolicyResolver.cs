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
        CancellationToken cancellationToken)
    {
        var resolvedDefinition = await ResolveDefinitionForRunAsync(
            agentDefinitionRepository,
            agentRunRepository,
            agentStepRepository,
            agentRun,
            cancellationToken);

        return resolvedDefinition is null
            ? null
            : ApprovalPolicyParser.ParseOptional(resolvedDefinition.Version.ApprovalPolicy);
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
}
