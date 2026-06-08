using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public sealed record RetrievalPayload(
    string Type,
    string QueryText);

public static class RetrievalPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string Create(string? queryText)
    {
        return JsonSerializer.Serialize(new RetrievalPayload(
            "retrieval",
            NormalizeRequired(queryText, nameof(queryText))));
    }

    public static RetrievalPayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Retrieval payload is missing.");
        }

        var retrievalPayload = JsonSerializer.Deserialize<RetrievalPayload>(payload, JsonOptions);
        if (retrievalPayload is null
            || !string.Equals(retrievalPayload.Type, "retrieval", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(retrievalPayload.QueryText))
        {
            throw new InvalidOperationException("Retrieval payload is invalid.");
        }

        return retrievalPayload with
        {
            QueryText = NormalizeRequired(retrievalPayload.QueryText, nameof(retrievalPayload.QueryText))
        };
    }

    public static bool TryParse(string? payload, out RetrievalPayload? retrievalPayload)
    {
        retrievalPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            retrievalPayload = Parse(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Serialize(RetrievalPayload payload)
    {
        return JsonSerializer.Serialize(payload with
        {
            QueryText = NormalizeRequired(payload.QueryText, nameof(payload.QueryText))
        });
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"Retrieval payload field '{fieldName}' is required.");
        }

        return normalized;
    }
}
