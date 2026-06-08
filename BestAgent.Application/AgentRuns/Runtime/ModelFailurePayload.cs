using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public sealed record ModelFailurePayload(
    string Type,
    string? ErrorCode,
    string Message);

public static class ModelFailurePayloadSerializer
{
    public static string Create(string? errorCode, string message)
    {
        return JsonSerializer.Serialize(new ModelFailurePayload(
            "model_failure",
            Normalize(errorCode),
            message.Trim()));
    }

    public static bool TryParse(string? payload, out ModelFailurePayload? modelFailurePayload)
    {
        modelFailurePayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            modelFailurePayload = JsonSerializer.Deserialize<ModelFailurePayload>(payload);
            return modelFailurePayload is not null
                && string.Equals(modelFailurePayload.Type, "model_failure", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(modelFailurePayload.Message);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
