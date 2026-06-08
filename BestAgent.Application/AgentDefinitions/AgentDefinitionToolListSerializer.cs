using System.Text.Json;

namespace BestAgent.Application.AgentDefinitions;

internal static class AgentDefinitionToolListSerializer
{
    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public static string? Serialize(IReadOnlyList<string>? values)
    {
        var normalized = values?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized is { Length: > 0 } ? JsonSerializer.Serialize(normalized) : null;
    }
}
