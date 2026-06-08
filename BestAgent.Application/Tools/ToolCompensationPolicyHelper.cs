using System.Text.Json;

namespace BestAgent.Application.Tools;

public static class ToolCompensationPolicyHelper
{
    public static string? GetModeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var normalized = ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
                value,
                nameof(value),
                "mode");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("mode", out var modeProperty)
                || modeProperty.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(modeProperty.GetString()))
            {
                return null;
            }

            return modeProperty.GetString()!.Trim().ToLowerInvariant();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsManual(string? value)
    {
        return string.Equals(GetModeOrNull(value), "manual", StringComparison.Ordinal);
    }
}
