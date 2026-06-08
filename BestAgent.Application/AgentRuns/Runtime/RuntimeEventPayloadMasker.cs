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

            if (jsonObject.TryGetPropertyValue("decisionPayload", out var decisionPayloadNode)
                && decisionPayloadNode is not null)
            {
                var rawDecisionPayload = decisionPayloadNode.GetValueKind() == JsonValueKind.String
                    ? decisionPayloadNode.GetValue<string?>()
                    : decisionPayloadNode.ToJsonString();
                jsonObject["decisionPayload"] = MaskDecisionPayload(rawDecisionPayload);
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
            Output = RuntimePayloadMasker.MaskToolOutput(data.Output),
            DecisionPayload = MaskDecisionPayload(data.DecisionPayload)
        };
    }

    private static string? MaskDecisionPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return payload;
        }

        if (ApprovalPayloadSerializer.TryParse(payload, out var approvalPayload)
            && approvalPayload is not null)
        {
            return ApprovalPayloadSerializer.Serialize(approvalPayload with
            {
                ToolInput = RuntimePayloadMasker.MaskToolInput(approvalPayload.ToolInput)
            });
        }

        if (HumanApprovalPayloadSerializer.TryParse(payload, out var humanPayload)
            && humanPayload is not null)
        {
            return HumanApprovalPayloadSerializer.Serialize(humanPayload with
            {
                SourceToolInput = RuntimePayloadMasker.MaskToolInput(humanPayload.SourceToolInput),
                SourceToolOutput = RuntimePayloadMasker.MaskToolOutput(humanPayload.SourceToolOutput)
            });
        }

        return payload;
    }
}
