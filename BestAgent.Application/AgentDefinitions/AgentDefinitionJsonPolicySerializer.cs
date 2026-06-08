using System.Text.Json;

namespace BestAgent.Application.AgentDefinitions;

internal static class AgentDefinitionJsonPolicySerializer
{
    public static string? NormalizeOptionalJson(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{fieldName} must be valid JSON.", exception);
        }
    }
}
