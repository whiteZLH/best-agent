using System.Text.Json;

namespace BestAgent.Application.Tools;

public static class ToolStructuredPolicyHelper
{
    public static string? NormalizeOptionalObjectOrLegacyString(
        string? value,
        string fieldName,
        string legacyPropertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!LooksLikeJson(trimmed))
        {
            return JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [legacyPropertyName] = trimmed
            });
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{fieldName} must be a JSON object.");
            }

            ValidateOptionalStringProperty(document.RootElement, fieldName, legacyPropertyName);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{fieldName} must be a JSON object or a legacy string value.", ex);
        }
    }

    private static bool LooksLikeJson(string value)
    {
        return value.StartsWith('{') || value.StartsWith('[') || value.StartsWith('"');
    }

    private static void ValidateOptionalStringProperty(
        JsonElement root,
        string fieldName,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException($"{fieldName}.{propertyName} must be non-empty string.");
        }
    }
}
