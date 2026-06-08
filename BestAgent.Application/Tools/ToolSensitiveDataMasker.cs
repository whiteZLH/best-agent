using System.Text.Json;
using System.Text.Json.Nodes;

namespace BestAgent.Application.Tools;

public static class ToolSensitiveDataMasker
{
    private const string MaskedValue = "***";
    private static readonly string[] RuntimeSensitivePropertyMarkers =
    [
        "authorization",
        "token",
        "apikey",
        "api_key",
        "secret",
        "password",
        "credential"
    ];

    public static string? MaskOptionalSecret(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : MaskedValue;
    }

    public static string? MaskAuthHeaders(string? authHeaders)
    {
        if (string.IsNullOrWhiteSpace(authHeaders))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(authHeaders);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return MaskedValue;
            }

            var payload = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                payload[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : MaskedValue;
            }

            return JsonSerializer.Serialize(payload);
        }
        catch (JsonException)
        {
            return MaskedValue;
        }
    }

    public static string? MaskExecutionBinding(string? executionKind, string? executionBinding)
    {
        var normalizedExecutionKind = ToolExecutionBindingHelper.NormalizeExecutionKind(
            executionKind,
            nameof(executionKind));
        if (string.IsNullOrWhiteSpace(executionBinding))
        {
            return executionBinding;
        }

        if (normalizedExecutionKind == ToolExecutionBindingHelper.Webhook)
        {
            var webhookBinding = ToolExecutionBindingHelper.ParseWebhookBinding(executionBinding, nameof(executionBinding));
            return ToolExecutionBindingHelper.CreateWebhookBinding(
                webhookBinding.EndpointUrl,
                webhookBinding.HttpMethod,
                MaskAuthHeaders(webhookBinding.AuthHeaders));
        }

        if (normalizedExecutionKind == ToolExecutionBindingHelper.InlineResult)
        {
            var inlineResultBinding = ToolExecutionBindingHelper.ParseInlineResultBinding(executionBinding, nameof(executionBinding));
            return ToolExecutionBindingHelper.CreateInlineResultBinding(
                MaskRuntimePayload(inlineResultBinding.Output) ?? inlineResultBinding.Output,
                MaskRuntimePayload(inlineResultBinding.Meta));
        }

        if (normalizedExecutionKind == ToolExecutionBindingHelper.LocalHandler)
        {
            var localHandlerBinding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(executionBinding, nameof(executionBinding));
            return ToolExecutionBindingHelper.CreateLocalHandlerBinding(localHandlerBinding.HandlerName);
        }

        return executionBinding;
    }

    public static string? MaskStructuredPolicyPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(payload);
            if (node is not JsonObject and not JsonArray)
            {
                return payload;
            }

            MaskRuntimeNode(node, parentPropertyName: null);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    public static string? MaskRuntimePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return payload;
        }

        try
        {
            var node = JsonNode.Parse(payload);
            if (node is not JsonObject and not JsonArray)
            {
                return payload;
            }

            MaskRuntimeNode(node, parentPropertyName: null);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static void MaskRuntimeNode(JsonNode? node, string? parentPropertyName)
    {
        if (node is null)
        {
            return;
        }

        if (ShouldMaskRuntimeProperty(parentPropertyName))
        {
            node.ReplaceWith(JsonValue.Create(MaskedValue));
            return;
        }

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                MaskRuntimeNode(property.Value, property.Key);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                MaskRuntimeNode(item, parentPropertyName);
            }
        }
    }

    private static bool ShouldMaskRuntimeProperty(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        return RuntimeSensitivePropertyMarkers.Any(marker =>
            propertyName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
