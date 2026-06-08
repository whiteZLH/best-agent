using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public sealed record ToolInvocationEventPayload(
    string InvocationId,
    string ToolName,
    string Mode,
    string Status,
    string CallbackToken);

public static class ToolInvocationEventPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string Create(
        string invocationId,
        string toolName,
        string mode,
        string status,
        string callbackToken)
    {
        return JsonSerializer.Serialize(new ToolInvocationEventPayload(
            NormalizeRequired(invocationId, nameof(invocationId)),
            NormalizeRequired(toolName, nameof(toolName)),
            NormalizeRequired(mode, nameof(mode)),
            NormalizeRequired(status, nameof(status)),
            NormalizeRequired(callbackToken, nameof(callbackToken))));
    }

    public static bool TryParse(string? payload, out ToolInvocationEventPayload? toolInvocation)
    {
        toolInvocation = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            toolInvocation = JsonSerializer.Deserialize<ToolInvocationEventPayload>(payload, JsonOptions);
            return toolInvocation is not null
                && !string.IsNullOrWhiteSpace(toolInvocation.InvocationId)
                && !string.IsNullOrWhiteSpace(toolInvocation.ToolName)
                && !string.IsNullOrWhiteSpace(toolInvocation.Mode)
                && !string.IsNullOrWhiteSpace(toolInvocation.Status)
                && !string.IsNullOrWhiteSpace(toolInvocation.CallbackToken);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"Tool invocation event field '{fieldName}' is required.");
        }

        return normalized;
    }
}
