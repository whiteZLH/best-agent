using System.Text.Json;
using System.Text.Json.Nodes;

namespace BestAgent.Application.Tools;

public static class ToolAuthPolicyHelper
{
    public const string None = "none";
    public const string Bearer = "bearer";
    public const string OAuth = "oauth";

    public static string? NormalizeOptionalPolicy(string? value, string fieldName)
    {
        var normalized = ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
            value,
            fieldName,
            "scheme");

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(normalized);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{fieldName} must be a JSON object.", ex);
        }

        if (node is not JsonObject jsonObject)
        {
            throw new InvalidOperationException($"{fieldName} must be a JSON object.");
        }

        if (!jsonObject.TryGetPropertyValue("scheme", out var schemeNode)
            || schemeNode is null
            || schemeNode.GetValueKind() != JsonValueKind.String
            || string.IsNullOrWhiteSpace(schemeNode.GetValue<string>()))
        {
            throw new InvalidOperationException($"{fieldName}.scheme is required.");
        }

        var normalizedScheme = NormalizeScheme(schemeNode.GetValue<string>(), fieldName);
        jsonObject["scheme"] = normalizedScheme;
        return jsonObject.ToJsonString();
    }

    public static void ValidateExecutionCompatibility(
        string? authPolicy,
        string? executionKind,
        string? authHeaders,
        string authPolicyFieldName,
        string executionKindFieldName,
        string authHeadersFieldName)
    {
        if (string.IsNullOrWhiteSpace(authPolicy))
        {
            return;
        }

        var normalizedExecutionKind = ToolExecutionBindingHelper.NormalizeExecutionKind(
            executionKind,
            executionKindFieldName);
        if (normalizedExecutionKind is null)
        {
            return;
        }

        var scheme = ParseScheme(authPolicy, authPolicyFieldName);
        if (!string.Equals(normalizedExecutionKind, ToolExecutionBindingHelper.Webhook, StringComparison.Ordinal))
        {
            if (!string.Equals(scheme, None, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{authPolicyFieldName}.scheme '{scheme}' is only supported for '{ToolExecutionBindingHelper.Webhook}' execution kind.");
            }

            return;
        }

        if (string.Equals(scheme, None, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(authHeaders))
            {
                throw new InvalidOperationException(
                    $"{authHeadersFieldName} must be omitted when {authPolicyFieldName}.scheme is '{None}'.");
            }

            return;
        }

        if (!HasBearerAuthorizationHeader(authHeaders))
        {
            throw new InvalidOperationException(
                $"{authHeadersFieldName} must include Authorization Bearer header when {authPolicyFieldName}.scheme is '{scheme}'.");
        }
    }

    private static string ParseScheme(string authPolicy, string fieldName)
    {
        using var document = JsonDocument.Parse(authPolicy);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("scheme", out var schemeProperty)
            || schemeProperty.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(schemeProperty.GetString()))
        {
            throw new InvalidOperationException($"{fieldName}.scheme is required.");
        }

        return NormalizeScheme(schemeProperty.GetString()!, fieldName);
    }

    private static string NormalizeScheme(string scheme, string fieldName)
    {
        var normalized = scheme.Trim().ToLowerInvariant();
        return normalized switch
        {
            None => None,
            Bearer => Bearer,
            OAuth => OAuth,
            _ => throw new InvalidOperationException(
                $"{fieldName}.scheme must be one of: {None}, {Bearer}, {OAuth}.")
        };
    }

    private static bool HasBearerAuthorizationHeader(string? authHeaders)
    {
        if (string.IsNullOrWhiteSpace(authHeaders))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(authHeaders);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    || property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                var trimmed = value.Trim();
                return trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(trimmed["Bearer ".Length..]);
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
