using System.Text.Json;
using BestAgent.Application.Tools;
using BestAgent.Domain.Tools;

namespace BestAgent.Infrastructure.Tools;

public class JsonSchemaToolOutputValidator : IToolOutputValidator
{
    public void Validate(ToolDefinition definition, string output)
    {
        if (string.IsNullOrWhiteSpace(definition.OutputSchema))
        {
            return;
        }

        var schema = JsonSchemaToolValidation.ParseSchema(definition.ToolName, definition.OutputSchema, "Output");
        var validationError = TryValidateCandidates(definition.ToolName, output, schema);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            throw new InvalidOperationException(validationError);
        }
    }

    private static string? TryValidateCandidates(string toolName, string output, JsonElement schema)
    {
        string? lastError = null;
        string? preferredError = null;
        JsonDocument? parsedOutput = null;
        var fallbackCandidates = new List<JsonDocument>();

        try
        {
            if (TryParseJson(output, out parsedOutput) && parsedOutput is not null)
            {
                if (JsonSchemaToolValidation.TryValidateElement(
                    toolName,
                    "$",
                    parsedOutput.RootElement,
                    schema,
                    out var parsedError,
                    "Output"))
                {
                    return null;
                }

                lastError = parsedError;
                preferredError ??= parsedError;

                if (parsedOutput.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    return preferredError ?? lastError ?? $"Output for tool '{toolName}' does not match the declared schema.";
                }
            }

            fallbackCandidates.Add(JsonDocument.Parse(JsonSerializer.Serialize(output)));

            foreach (var candidate in fallbackCandidates)
            {
                if (JsonSchemaToolValidation.TryValidateElement(
                    toolName,
                    "$",
                    candidate.RootElement,
                    schema,
                    out var error,
                    "Output"))
                {
                    return null;
                }

                lastError = error;
                preferredError ??= error;
            }

            return preferredError ?? lastError ?? $"Output for tool '{toolName}' does not match the declared schema.";
        }
        finally
        {
            parsedOutput?.Dispose();
            foreach (var candidate in fallbackCandidates)
            {
                candidate.Dispose();
            }
        }
    }

    private static bool TryParseJson(string output, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(output);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }
}
