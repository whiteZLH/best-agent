using BestAgent.Application.Tools;

namespace BestAgent.Application.AgentRuns.Approvals;

public static class ApprovalPolicyOptionsNormalizer
{
    public static ApprovalPolicyOptions Normalize(ApprovalPolicyOptions? options)
    {
        var source = options ?? new ApprovalPolicyOptions();

        return new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = NormalizeSideEffectLevels(
                source.ApprovalRequiredSideEffectLevels,
                nameof(ApprovalPolicyOptions.ApprovalRequiredSideEffectLevels)),
            RoleRequiredSideEffectLevels = NormalizeSideEffectLevels(
                source.RoleRequiredSideEffectLevels,
                nameof(ApprovalPolicyOptions.RoleRequiredSideEffectLevels)),
            AllowedApproverRoles = NormalizeRoles(source.AllowedApproverRoles),
            ParameterApprovalRules = NormalizeParameterApprovalRules(source.ParameterApprovalRules)
        };
    }

    private static string[] NormalizeSideEffectLevels(IReadOnlyList<string>? values, string fieldName)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        return values
            .Select((value, index) => ToolDefinitionPolicyValidator.NormalizeSideEffectLevel(value, $"{fieldName}[{index}]"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeRoles(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var role = value.Trim();
            if (normalized.Any(existing => string.Equals(existing, role, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalized.Add(role);
        }

        return normalized.ToArray();
    }

    private static ApprovalParameterRule[] NormalizeParameterApprovalRules(IReadOnlyList<ApprovalParameterRule>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return [];
        }

        var normalized = new List<ApprovalParameterRule>(rules.Count);
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            if (string.IsNullOrWhiteSpace(rule.ToolName))
            {
                throw new InvalidOperationException($"ParameterApprovalRules[{index}].ToolName is required.");
            }

            if (string.IsNullOrWhiteSpace(rule.InputPath))
            {
                throw new InvalidOperationException($"ParameterApprovalRules[{index}].InputPath is required.");
            }

            normalized.Add(new ApprovalParameterRule
            {
                ToolName = rule.ToolName.Trim(),
                InputPath = rule.InputPath.Trim(),
                ExpectedValue = string.IsNullOrWhiteSpace(rule.ExpectedValue) ? null : rule.ExpectedValue.Trim(),
                OverrideSideEffectLevel = string.IsNullOrWhiteSpace(rule.OverrideSideEffectLevel)
                    ? null
                    : ToolDefinitionPolicyValidator.NormalizeSideEffectLevel(
                        rule.OverrideSideEffectLevel,
                        $"ParameterApprovalRules[{index}].OverrideSideEffectLevel")
            });
        }

        return normalized.ToArray();
    }
}
