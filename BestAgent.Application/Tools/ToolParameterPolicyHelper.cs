using System.Text.Json;

namespace BestAgent.Application.Tools;

public static class ToolParameterPolicyHelper
{
    public static string? NormalizeOptionalPolicy(string? parameterPolicy, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(parameterPolicy))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(parameterPolicy.Trim());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{fieldName} must be a JSON object.");
            }

            var allowedPaths = ReadStringArray(document.RootElement, "allowedPaths", fieldName);
            var deniedPaths = ReadStringArray(document.RootElement, "deniedPaths", fieldName);
            if (allowedPaths.Count == 0 && deniedPaths.Count == 0)
            {
                throw new InvalidOperationException($"{fieldName} must include at least one allowedPaths or deniedPaths entry.");
            }

            return JsonSerializer.Serialize(new Dictionary<string, string[]>
            {
                ["allowedPaths"] = [.. allowedPaths],
                ["deniedPaths"] = [.. deniedPaths]
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{fieldName} must be a JSON object.", ex);
        }
    }

    public static ToolParameterPolicy? ParseOptional(string? parameterPolicy)
    {
        if (string.IsNullOrWhiteSpace(parameterPolicy))
        {
            return null;
        }

        var normalized = NormalizeOptionalPolicy(parameterPolicy, nameof(parameterPolicy));
        using var document = JsonDocument.Parse(normalized!);
        var allowedPaths = ReadStringArray(document.RootElement, "allowedPaths", nameof(parameterPolicy));
        var deniedPaths = ReadStringArray(document.RootElement, "deniedPaths", nameof(parameterPolicy));
        return new ToolParameterPolicy(allowedPaths, deniedPaths);
    }

    public static void ValidateInput(string toolName, string? parameterPolicy, string? input)
    {
        var policy = ParseOptional(parameterPolicy);
        if (policy is null)
        {
            return;
        }

        using var document = ToolInputPathHelper.TryParseJson(input);
        if (document is null)
        {
            return;
        }

        foreach (var path in ToolInputPathHelper.EnumerateLeafPaths(document.RootElement))
        {
            if (policy.DeniedPaths.Any(deniedPath => PathMatches(deniedPath, path)))
            {
                throw new InvalidOperationException(
                    $"Input for tool '{toolName}' contains denied parameter path '$.{path}'.");
            }

            if (policy.AllowedPaths.Count > 0
                && !policy.AllowedPaths.Any(allowedPath => PathMatches(allowedPath, path)))
            {
                throw new InvalidOperationException(
                    $"Input for tool '{toolName}' contains parameter path '$.{path}' which is not allowed by parameter policy.");
            }
        }
    }

    private static bool PathMatches(string configuredPath, string actualPath)
    {
        if (string.Equals(configuredPath, actualPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return actualPath.StartsWith($"{configuredPath}.", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName, string fieldName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{fieldName}.{propertyName} must be an array of non-empty strings.");
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new InvalidOperationException($"{fieldName}.{propertyName} must be an array of non-empty strings.");
            }

            var value = item.GetString()!.Trim();
            if (!values.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(value);
            }
        }

        return values;
    }
}

public sealed record ToolParameterPolicy(
    IReadOnlyList<string> AllowedPaths,
    IReadOnlyList<string> DeniedPaths);
