using System.Text.Json;
using System.Text.Json.Nodes;

namespace BestAgent.Application.AgentRuns.Runtime;

public static class RuntimeEventPayloadMasker
{
    public static string? MaskPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return payload;
        }

        try
        {
            var node = JsonNode.Parse(payload);
            if (node is not JsonObject jsonObject)
            {
                return payload;
            }

            if (jsonObject.TryGetPropertyValue("output", out var outputNode)
                && outputNode is not null)
            {
                var rawOutput = outputNode.GetValueKind() == JsonValueKind.String
                    ? outputNode.GetValue<string?>()
                    : outputNode.ToJsonString();
                var maskedOutput = RuntimePayloadMasker.MaskToolOutput(rawOutput);
                jsonObject["output"] = maskedOutput;
            }

            return jsonObject.ToJsonString();
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    public static AgentRunEventData MaskEventData(AgentRunEventData data)
    {
        return data with
        {
            Output = RuntimePayloadMasker.MaskToolOutput(data.Output)
        };
    }
}
