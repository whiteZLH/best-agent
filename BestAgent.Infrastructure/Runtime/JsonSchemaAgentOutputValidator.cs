using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Infrastructure.Tools;

namespace BestAgent.Infrastructure.Runtime;

public class JsonSchemaAgentOutputValidator : IAgentOutputValidator
{
    public void Validate(string agentCode, string? outputSchema, string output)
    {
        if (string.IsNullOrWhiteSpace(outputSchema))
        {
            return;
        }

        try
        {
            var schema = JsonSchemaToolValidation.ParseSchema(agentCode, outputSchema, "Output");
            var validationError = TryValidateCandidates(agentCode, output, schema);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                throw new InvalidOperationException(validationError);
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(RewriteMessage(agentCode, ex.Message), ex);
        }
    }

    private static string? TryValidateCandidates(string agentCode, string output, JsonElement schema)
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
                    agentCode,
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
                    return preferredError ?? lastError ?? $"Output for agent '{agentCode}' does not match the declared schema.";
                }
            }

            fallbackCandidates.Add(JsonDocument.Parse(JsonSerializer.Serialize(output)));

            foreach (var candidate in fallbackCandidates)
            {
                if (JsonSchemaToolValidation.TryValidateElement(
                    agentCode,
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

            return preferredError ?? lastError ?? $"Output for agent '{agentCode}' does not match the declared schema.";
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

    private static string RewriteMessage(string agentCode, string message)
    {
        return message
            .Replace($"schema for tool '{agentCode}'", $"schema for agent '{agentCode}'", StringComparison.Ordinal)
            .Replace($"for tool '{agentCode}'", $"for agent '{agentCode}'", StringComparison.Ordinal)
            .Replace($"tool '{agentCode}'", $"agent '{agentCode}'", StringComparison.Ordinal);
    }
}
