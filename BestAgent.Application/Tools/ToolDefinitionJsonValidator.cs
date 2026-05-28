using System.Text.Json;

namespace BestAgent.Application.Tools;

public static class ToolDefinitionJsonValidator
{
    public static string? NormalizeOptionalJson(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.GetRawText();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{fieldName} must be valid JSON.", ex);
        }
    }

    public static string? NormalizeOptionalJsonObject(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizeOptionalJson(value, fieldName);
        using var document = JsonDocument.Parse(normalized!);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{fieldName} must be a JSON object.");
        }

        return normalized;
    }
}
