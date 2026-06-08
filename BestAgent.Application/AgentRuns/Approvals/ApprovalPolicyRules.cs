using BestAgent.Application.Tools;
using BestAgent.Domain.Tools;
using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Approvals;

public static class ApprovalPolicyRules
{
    public static bool RequiresApproval(ToolDefinition? toolDefinition, ApprovalPolicyOptions? options = null)
    {
        return EvaluateApprovalRequirement(toolDefinition, null, options).RequiresApproval;
    }

    public static ApprovalRequirement EvaluateApprovalRequirement(
        ToolDefinition? toolDefinition,
        string? toolInput,
        ApprovalPolicyOptions? options = null)
    {
        var normalizedOptions = ApprovalPolicyOptionsNormalizer.Normalize(options);
        if (toolDefinition is null)
        {
            return ApprovalRequirement.None;
        }

        var effectiveSideEffectLevel = toolDefinition.SideEffectLevel;
        if (Contains(
            normalizedOptions.ApprovalRequiredSideEffectLevels,
            effectiveSideEffectLevel))
        {
            return new ApprovalRequirement(true, effectiveSideEffectLevel);
        }

        var matchedRule = FindMatchingParameterRule(toolDefinition.ToolName, toolInput, normalizedOptions.ParameterApprovalRules);
        if (matchedRule is null)
        {
            return new ApprovalRequirement(false, effectiveSideEffectLevel);
        }

        return new ApprovalRequirement(
            true,
            string.IsNullOrWhiteSpace(matchedRule.OverrideSideEffectLevel)
                ? effectiveSideEffectLevel
                : matchedRule.OverrideSideEffectLevel.Trim());
    }

    public static bool RequiresApprovalRole(string? sideEffectLevel, ApprovalPolicyOptions? options = null)
    {
        return Contains(ApprovalPolicyOptionsNormalizer.Normalize(options).RoleRequiredSideEffectLevels, sideEffectLevel);
    }

    public static bool HasAllowedApproverRole(string? approverRole, ApprovalPolicyOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(approverRole))
        {
            return false;
        }

        var allowedRoles = ApprovalPolicyOptionsNormalizer.Normalize(options).AllowedApproverRoles;
        return approverRole
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(role => Contains(allowedRoles, role));
    }

    private static bool Contains(IReadOnlyList<string> values, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return values.Any(value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static ApprovalParameterRule? FindMatchingParameterRule(
        string toolName,
        string? toolInput,
        IReadOnlyList<ApprovalParameterRule>? rules)
    {
        if (string.IsNullOrWhiteSpace(toolInput) || rules is null || rules.Count == 0)
        {
            return null;
        }

        using var document = TryParseJson(toolInput);
        if (document is null)
        {
            return null;
        }

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.ToolName)
                || string.IsNullOrWhiteSpace(rule.InputPath)
                || !string.Equals(rule.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryResolvePath(document.RootElement, rule.InputPath, out var value))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.ExpectedValue)
                || string.Equals(FormatValue(value), rule.ExpectedValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    private static JsonDocument? TryParseJson(string toolInput)
    {
        return ToolInputPathHelper.TryParseJson(toolInput);
    }

    private static bool TryResolvePath(JsonElement root, string inputPath, out JsonElement value)
    {
        return ToolInputPathHelper.TryResolvePath(root, inputPath, out value);
    }

    private static string FormatValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };
    }
}

public readonly record struct ApprovalRequirement(bool RequiresApproval, string? SideEffectLevel)
{
    public static ApprovalRequirement None { get; } = new(false, null);
}
