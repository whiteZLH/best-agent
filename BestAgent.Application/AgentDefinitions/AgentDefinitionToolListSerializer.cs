using System.Text.Json;

namespace BestAgent.Application.AgentDefinitions;

internal static class AgentDefinitionToolListSerializer
{
    public static IReadOnlyList<string> Parse(string? allowedTools)
    {
        if (string.IsNullOrWhiteSpace(allowedTools))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(allowedTools) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public static string? Serialize(IReadOnlyList<string>? allowedTools)
    {
        var normalized = allowedTools?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized is { Length: > 0 } ? JsonSerializer.Serialize(normalized) : null;
    }
}
