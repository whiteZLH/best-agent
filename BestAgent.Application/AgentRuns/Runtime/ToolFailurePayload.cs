using System.Text.Json;
using BestAgent.Application.Tools;

namespace BestAgent.Application.AgentRuns.Runtime;

using System.Text.Json.Serialization;

public sealed record ToolFailurePayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("toolName")] string ToolName,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("compensation")] ToolFailureCompensationPayload? Compensation = null);

public sealed record ToolFailureCompensationPayload(
    [property: JsonPropertyName("mode")] string Mode);

public static class ToolFailurePayloadSerializer
{
    public static string Create(string toolName, string stage, string message, string? compensationPolicy = null)
    {
        return JsonSerializer.Serialize(new ToolFailurePayload(
            "tool_error",
            toolName,
            stage,
            message,
            CreateCompensationPayload(compensationPolicy)));
    }

    public static string ExtractMessage(string? payloadOrMessage)
    {
        if (TryParse(payloadOrMessage, out var payload) && payload is not null)
        {
            return payload.Message;
        }

        return payloadOrMessage ?? string.Empty;
    }

    public static bool TryParse(string? payload, out ToolFailurePayload? toolFailurePayload)
    {
        toolFailurePayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            toolFailurePayload = JsonSerializer.Deserialize<ToolFailurePayload>(payload);
            return toolFailurePayload is not null
                && string.Equals(toolFailurePayload.Type, "tool_error", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(toolFailurePayload.ToolName)
                && !string.IsNullOrWhiteSpace(toolFailurePayload.Stage)
                && !string.IsNullOrWhiteSpace(toolFailurePayload.Message);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ToolFailureCompensationPayload? CreateCompensationPayload(string? compensationPolicy)
    {
        var mode = ToolCompensationPolicyHelper.GetModeOrNull(compensationPolicy);
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        return new ToolFailureCompensationPayload(mode);
    }
}
