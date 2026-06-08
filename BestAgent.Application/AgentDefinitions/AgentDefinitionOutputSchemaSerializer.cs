using BestAgent.Application.Tools;
using System.Text.Json;

namespace BestAgent.Application.AgentDefinitions;

internal static class AgentDefinitionOutputSchemaSerializer
{
    public static string? NormalizeOptional(string? outputSchema)
    {
        var normalized = ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(outputSchema, "Output schema");
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        using var document = JsonDocument.Parse(normalized);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
