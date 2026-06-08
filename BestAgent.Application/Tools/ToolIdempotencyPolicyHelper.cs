using System.Text.Json;

namespace BestAgent.Application.Tools;

public static class ToolIdempotencyPolicyHelper
{
    public static string? NormalizeOptionalPolicy(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "idempotent", StringComparison.OrdinalIgnoreCase))
        {
            return "idempotent";
        }

        if (string.Equals(trimmed, "non-idempotent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "non-idempotent";
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{fieldName} must be 'idempotent', 'non-idempotent', or a JSON object.");
            }

            ValidateObject(document.RootElement, fieldName);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{fieldName} must be 'idempotent', 'non-idempotent', or a JSON object.", ex);
        }
    }

    public static bool IsEnabled(string toolName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "idempotent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(trimmed, "non-idempotent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"IdempotencyPolicy for tool '{toolName}' must be 'idempotent', 'non-idempotent', or a JSON object.");
            }

            ValidateObject(document.RootElement, $"IdempotencyPolicy for tool '{toolName}'");
            if (document.RootElement.TryGetProperty("enabled", out var enabledElement))
            {
                return enabledElement.ValueKind == JsonValueKind.True;
            }

            return true;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"IdempotencyPolicy for tool '{toolName}' must be 'idempotent', 'non-idempotent', or a JSON object.", ex);
        }
    }

    private static void ValidateObject(JsonElement root, string fieldName)
    {
        if (!root.TryGetProperty("enabled", out var enabledElement))
        {
            return;
        }

        if (enabledElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException($"{fieldName}.enabled must be boolean.");
        }
    }
}
