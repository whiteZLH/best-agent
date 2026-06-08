using System.Text.Json;
using BestAgent.Application.Tools;
using BestAgent.Domain.Tools;

namespace BestAgent.Infrastructure.Tools;

public class JsonSchemaToolInputValidator : IToolInputValidator
{
    public void Validate(ToolDefinition definition, string? input)
    {
        if (string.IsNullOrWhiteSpace(definition.InputSchema))
        {
            return;
        }

        using var inputDocument = ParseInput(definition, input);
        var schema = JsonSchemaToolValidation.ParseSchema(definition.ToolName, definition.InputSchema, "Input");
        if (!JsonSchemaToolValidation.TryValidateElement(
            definition.ToolName,
            "$",
            inputDocument.RootElement,
            schema,
            out var error,
            "Input"))
        {
            throw new InvalidOperationException(error);
        }
    }

    private static System.Text.Json.JsonDocument ParseInput(ToolDefinition definition, string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            input = "null";
        }

        try
        {
            return JsonDocument.Parse(input);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Input for tool '{definition.ToolName}' must be valid JSON.", ex);
        }
    }
}
