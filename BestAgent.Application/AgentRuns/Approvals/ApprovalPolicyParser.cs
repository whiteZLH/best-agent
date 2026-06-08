using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Approvals;

public static class ApprovalPolicyParser
{
    public static ApprovalPolicyOptions? ParseOptional(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("ApprovalPolicy must be a JSON object.");
            }

            var root = document.RootElement;
            return new ApprovalPolicyOptions
            {
                ApprovalRequiredSideEffectLevels = ReadStringList(
                    root,
                    "approvalRequiredSideEffectLevels",
                    "ApprovalRequiredSideEffectLevels"),
                RoleRequiredSideEffectLevels = ReadStringList(
                    root,
                    "roleRequiredSideEffectLevels",
                    "RoleRequiredSideEffectLevels"),
                AllowedApproverRoles = ReadStringList(
                    root,
                    "allowedApproverRoles",
                    "AllowedApproverRoles"),
                ParameterApprovalRules = ReadParameterRules(
                    root,
                    "parameterApprovalRules",
                    "ParameterApprovalRules")
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("ApprovalPolicy must be a valid JSON object.", ex);
        }
    }

    public static ApprovalPolicyOptions Merge(
        ApprovalPolicyOptions? fallback,
        ApprovalPolicyOptions? overrideOptions)
    {
        if (overrideOptions is null)
        {
            return ApprovalPolicyOptionsNormalizer.Normalize(fallback);
        }

        var normalizedFallback = ApprovalPolicyOptionsNormalizer.Normalize(fallback);
        var hasApprovalLevels = overrideOptions.ApprovalRequiredSideEffectLevels.Count > 0;
        var hasRoleLevels = overrideOptions.RoleRequiredSideEffectLevels.Count > 0;
        var hasAllowedRoles = overrideOptions.AllowedApproverRoles.Count > 0;
        var hasParameterRules = overrideOptions.ParameterApprovalRules.Count > 0;

        return ApprovalPolicyOptionsNormalizer.Normalize(new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = hasApprovalLevels
                ? overrideOptions.ApprovalRequiredSideEffectLevels
                : normalizedFallback.ApprovalRequiredSideEffectLevels,
            RoleRequiredSideEffectLevels = hasRoleLevels
                ? overrideOptions.RoleRequiredSideEffectLevels
                : normalizedFallback.RoleRequiredSideEffectLevels,
            AllowedApproverRoles = hasAllowedRoles
                ? overrideOptions.AllowedApproverRoles
                : normalizedFallback.AllowedApproverRoles,
            ParameterApprovalRules = hasParameterRules
                ? overrideOptions.ParameterApprovalRules
                : normalizedFallback.ParameterApprovalRules
        });
    }

    private static IReadOnlyList<string> ReadStringList(
        JsonElement root,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return property
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();
        }

        return [];
    }

    private static IReadOnlyList<ApprovalParameterRule> ReadParameterRules(
        JsonElement root,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return property
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new ApprovalParameterRule
                {
                    ToolName = ReadOptionalString(item, "toolName", "ToolName") ?? string.Empty,
                    InputPath = ReadOptionalString(item, "inputPath", "InputPath") ?? string.Empty,
                    ExpectedValue = ReadOptionalString(item, "expectedValue", "ExpectedValue"),
                    OverrideSideEffectLevel = ReadOptionalString(item, "overrideSideEffectLevel", "OverrideSideEffectLevel")
                })
                .Where(rule =>
                    !string.IsNullOrWhiteSpace(rule.ToolName)
                    && !string.IsNullOrWhiteSpace(rule.InputPath))
                .ToArray();
        }

        return [];
    }

    private static string? ReadOptionalString(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }

        return null;
    }
}
