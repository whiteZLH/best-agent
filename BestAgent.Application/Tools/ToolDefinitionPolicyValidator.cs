namespace BestAgent.Application.Tools;

public static class ToolDefinitionPolicyValidator
{
    private static readonly HashSet<string> AllowedConsistencyModes = new(StringComparer.Ordinal)
    {
        "none",
        "eventual",
        "strong"
    };

    private static readonly HashSet<string> AllowedSideEffectLevels = new(StringComparer.Ordinal)
    {
        "read_only",
        "internal_write",
        "external_write",
        "destructive"
    };

    public static string NormalizeRequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        return value.Trim();
    }

    public static string NormalizeConsistencyMode(string? value, string fieldName)
    {
        var normalized = NormalizeRequiredText(value, fieldName).ToLowerInvariant();
        if (!AllowedConsistencyModes.Contains(normalized))
        {
            throw new InvalidOperationException($"{fieldName} must be one of: none, eventual, strong.");
        }

        return normalized;
    }

    public static string NormalizeSideEffectLevel(string? value, string fieldName)
    {
        var normalized = NormalizeRequiredText(value, fieldName).ToLowerInvariant();
        normalized = normalized switch
        {
            "readonly" => "read_only",
            "read_only" => "read_only",
            "internalwrite" => "internal_write",
            "internal_write" => "internal_write",
            "externalwrite" => "external_write",
            "external_write" => "external_write",
            _ => normalized
        };

        if (!AllowedSideEffectLevels.Contains(normalized))
        {
            throw new InvalidOperationException($"{fieldName} must be one of: read_only, internal_write, external_write, destructive.");
        }

        return normalized;
    }

    public static void ValidateCompensationPolicyRequirement(
        string sideEffectLevel,
        string? compensationPolicy,
        string compensationPolicyFieldName)
    {
        if (string.Equals(sideEffectLevel, "destructive", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(compensationPolicy))
        {
            throw new InvalidOperationException($"{compensationPolicyFieldName} is required for destructive tools.");
        }
    }
}
