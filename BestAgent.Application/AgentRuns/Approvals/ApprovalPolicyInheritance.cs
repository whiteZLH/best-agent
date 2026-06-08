namespace BestAgent.Application.AgentRuns.Approvals;

public static class ApprovalPolicyInheritance
{
    public static ApprovalPolicyOptions MergeStricter(
        ApprovalPolicyOptions? parentPolicy,
        ApprovalPolicyOptions? childPolicy)
    {
        var normalizedParent = ApprovalPolicyOptionsNormalizer.Normalize(parentPolicy);
        var normalizedChild = ApprovalPolicyOptionsNormalizer.Normalize(childPolicy);

        return ApprovalPolicyOptionsNormalizer.Normalize(new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = MergeSideEffectLevels(
                normalizedParent.ApprovalRequiredSideEffectLevels,
                normalizedChild.ApprovalRequiredSideEffectLevels),
            RoleRequiredSideEffectLevels = MergeSideEffectLevels(
                normalizedParent.RoleRequiredSideEffectLevels,
                normalizedChild.RoleRequiredSideEffectLevels),
            AllowedApproverRoles = MergeAllowedApproverRoles(
                normalizedParent.AllowedApproverRoles,
                normalizedChild.AllowedApproverRoles),
            ParameterApprovalRules = MergeParameterRules(
                normalizedParent.ParameterApprovalRules,
                normalizedChild.ParameterApprovalRules)
        });
    }

    private static IReadOnlyList<string> MergeSideEffectLevels(
        IReadOnlyList<string> parentLevels,
        IReadOnlyList<string> childLevels)
    {
        return parentLevels
            .Concat(childLevels)
            .Where(level => !string.IsNullOrWhiteSpace(level))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> MergeAllowedApproverRoles(
        IReadOnlyList<string> parentRoles,
        IReadOnlyList<string> childRoles)
    {
        var restricted = parentRoles
            .Where(role => childRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return restricted.Length == 0 ? Array.Empty<string>() : restricted;
    }

    private static IReadOnlyList<ApprovalParameterRule> MergeParameterRules(
        IReadOnlyList<ApprovalParameterRule> parentRules,
        IReadOnlyList<ApprovalParameterRule> childRules)
    {
        return parentRules
            .Concat(childRules)
            .GroupBy(
                rule => BuildParameterRuleKey(rule),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string BuildParameterRuleKey(ApprovalParameterRule rule)
    {
        return $"{rule.ToolName}|{rule.InputPath}|{rule.ExpectedValue}|{rule.OverrideSideEffectLevel}";
    }
}
