using System.Text.Json;

namespace BestAgent.Application.Tools;

public static class ToolRetryPolicyHelper
{
    public static string? NormalizeOptionalPolicy(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "retry-once", StringComparison.OrdinalIgnoreCase))
        {
            return """{"maxAttempts":2,"delayMs":0}""";
        }

        if (string.Equals(trimmed, "retry-twice", StringComparison.OrdinalIgnoreCase))
        {
            return """{"maxAttempts":3,"delayMs":0}""";
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{fieldName} must be 'retry-once', 'retry-twice', or a JSON object.");
            }

            ValidateObject(document.RootElement, fieldName);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{fieldName} must be 'retry-once', 'retry-twice', or a JSON object.", ex);
        }
    }

    private static void ValidateObject(JsonElement root, string fieldName)
    {
        ValidateIntegerProperty(root, "maxAttempts", fieldName, minimumValue: 1);
        ValidateIntegerProperty(root, "delayMs", fieldName, minimumValue: 0);
    }

    private static void ValidateIntegerProperty(
        JsonElement root,
        string propertyName,
        string fieldName,
        int minimumValue)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException($"{fieldName}.{propertyName} must be integer.");
        }

        if (value < minimumValue)
        {
            var comparator = minimumValue == 0 ? "non-negative" : $">= {minimumValue}";
            throw new InvalidOperationException($"{fieldName}.{propertyName} must be {comparator}.");
        }
    }
}
