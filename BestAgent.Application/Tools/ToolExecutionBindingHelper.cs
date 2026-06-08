using System.Text.Json;

namespace BestAgent.Application.Tools;

public static class ToolExecutionBindingHelper
{
    public const int CurrentBindingVersion = 1;
    public const string Webhook = "webhook";
    public const string LocalHandler = "local_handler";
    public const string InlineResult = "inline_result";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? NormalizeExecutionKind(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            Webhook => Webhook,
            LocalHandler => LocalHandler,
            InlineResult => InlineResult,
            _ => throw new InvalidOperationException(
                $"{fieldName} must be '{Webhook}', '{LocalHandler}' or '{InlineResult}'.")
        };
    }

    public static string CreateWebhookBinding(string endpointUrl, string httpMethod, string? authHeaders)
    {
        var payload = new VersionedExecutionBindingDocument(
            CurrentBindingVersion,
            Webhook,
            new WebhookExecutionBinding(endpointUrl, httpMethod, authHeaders),
            null,
            null);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string CreateLocalHandlerBinding(string handlerName)
    {
        var payload = new VersionedExecutionBindingDocument(
            CurrentBindingVersion,
            LocalHandler,
            null,
            new LocalHandlerExecutionBinding(handlerName),
            null);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string CreateInlineResultBinding(string output, string? meta = null)
    {
        var payload = new VersionedExecutionBindingDocument(
            CurrentBindingVersion,
            InlineResult,
            null,
            null,
            new InlineResultExecutionBinding(output, meta));
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static WebhookExecutionBinding ParseWebhookBinding(string? binding, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            throw new InvalidOperationException($"{fieldName} is required for '{Webhook}' execution kind.");
        }

        var payload = TryParseVersionedBinding(binding, fieldName);
        if (payload is not null)
        {
            if (!string.Equals(payload.Type, Webhook, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{fieldName} type must be '{Webhook}'.");
            }

            return NormalizeWebhookBinding(payload.Webhook, fieldName);
        }

        var legacyPayload = JsonSerializer.Deserialize<WebhookExecutionBinding>(binding, JsonOptions);
        return NormalizeWebhookBinding(legacyPayload, fieldName);
    }

    public static LocalHandlerExecutionBinding ParseLocalHandlerBinding(string? binding, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            throw new InvalidOperationException($"{fieldName} is required for '{LocalHandler}' execution kind.");
        }

        var payload = TryParseVersionedBinding(binding, fieldName);
        if (payload is not null)
        {
            if (!string.Equals(payload.Type, LocalHandler, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{fieldName} type must be '{LocalHandler}'.");
            }

            return NormalizeLocalHandlerBinding(payload.LocalHandler, fieldName);
        }

        var legacyPayload = JsonSerializer.Deserialize<LocalHandlerExecutionBinding>(binding, JsonOptions);
        return NormalizeLocalHandlerBinding(legacyPayload, fieldName);
    }

    public static InlineResultExecutionBinding ParseInlineResultBinding(string? binding, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            throw new InvalidOperationException($"{fieldName} is required for '{InlineResult}' execution kind.");
        }

        var payload = TryParseVersionedBinding(binding, fieldName);
        if (payload is not null)
        {
            if (!string.Equals(payload.Type, InlineResult, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{fieldName} type must be '{InlineResult}'.");
            }

            return NormalizeInlineResultBinding(payload.InlineResult, fieldName);
        }

        var legacyPayload = JsonSerializer.Deserialize<InlineResultExecutionBinding>(binding, JsonOptions);
        return NormalizeInlineResultBinding(legacyPayload, fieldName);
    }

    public static PersistedToolExecutionSettings ResolvePersistedExecutionSettings(
        string? executionKind,
        string? executionBinding,
        string? endpointUrl,
        string? httpMethod,
        string? authHeaders,
        string executionKindFieldName,
        string executionBindingFieldName,
        string endpointUrlFieldName,
        string httpMethodFieldName,
        string authHeadersFieldName)
    {
        var hasLegacyEndpointUrl = !string.IsNullOrWhiteSpace(endpointUrl);
        var hasLegacyHttpMethod = !string.IsNullOrWhiteSpace(httpMethod);
        var hasLegacyAuthHeaders = !string.IsNullOrWhiteSpace(authHeaders);
        var normalizedLegacyEndpointUrl = string.IsNullOrWhiteSpace(endpointUrl) ? null : endpointUrl.Trim();
        var normalizedLegacyHttpMethod = string.IsNullOrWhiteSpace(httpMethod) ? "POST" : httpMethod.Trim().ToUpperInvariant();
        var normalizedLegacyAuthHeaders = ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(authHeaders, authHeadersFieldName);
        var normalizedExecutionKind = NormalizeExecutionKind(executionKind, executionKindFieldName);
        var normalizedExecutionBinding = ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(executionBinding, executionBindingFieldName);

        if (normalizedExecutionKind is null && normalizedExecutionBinding is not null)
        {
            throw new InvalidOperationException($"{executionKindFieldName} is required when {executionBindingFieldName} is provided.");
        }

        if (normalizedExecutionKind is null && !string.IsNullOrWhiteSpace(normalizedLegacyEndpointUrl))
        {
            normalizedExecutionKind = Webhook;
            normalizedExecutionBinding = CreateWebhookBinding(
                normalizedLegacyEndpointUrl,
                normalizedLegacyHttpMethod,
                normalizedLegacyAuthHeaders);
        }

        string? persistedExecutionKind = null;
        string? persistedExecutionBinding = null;
        string? persistedEndpointUrl = normalizedLegacyEndpointUrl;
        var persistedHttpMethod = normalizedLegacyHttpMethod;
        string? persistedAuthHeaders = normalizedLegacyAuthHeaders;

        if (normalizedExecutionKind == Webhook)
        {
            var webhookBinding = ParseWebhookBinding(normalizedExecutionBinding, executionBindingFieldName);
            EnsureWebhookLegacyFieldsMatchExplicitBinding(
                webhookBinding,
                normalizedLegacyEndpointUrl,
                normalizedLegacyHttpMethod,
                normalizedLegacyAuthHeaders,
                hasLegacyEndpointUrl,
                hasLegacyHttpMethod,
                hasLegacyAuthHeaders,
                executionBindingFieldName,
                endpointUrlFieldName,
                httpMethodFieldName,
                authHeadersFieldName);
            persistedExecutionKind = Webhook;
            persistedExecutionBinding = CreateWebhookBinding(
                webhookBinding.EndpointUrl,
                webhookBinding.HttpMethod,
                webhookBinding.AuthHeaders);
            persistedEndpointUrl = webhookBinding.EndpointUrl;
            persistedHttpMethod = webhookBinding.HttpMethod;
            persistedAuthHeaders = webhookBinding.AuthHeaders;
        }
        else if (normalizedExecutionKind == LocalHandler)
        {
            var localBinding = ParseLocalHandlerBinding(normalizedExecutionBinding, executionBindingFieldName);
            EnsureLegacyWebhookFieldsOmittedForNonWebhookBinding(
                LocalHandler,
                hasLegacyEndpointUrl,
                hasLegacyHttpMethod,
                hasLegacyAuthHeaders,
                endpointUrlFieldName,
                httpMethodFieldName,
                authHeadersFieldName);
            persistedExecutionKind = LocalHandler;
            persistedExecutionBinding = CreateLocalHandlerBinding(localBinding.HandlerName);
            persistedEndpointUrl = null;
            persistedHttpMethod = "POST";
            persistedAuthHeaders = null;
        }
        else if (normalizedExecutionKind == InlineResult)
        {
            var inlineBinding = ParseInlineResultBinding(normalizedExecutionBinding, executionBindingFieldName);
            EnsureLegacyWebhookFieldsOmittedForNonWebhookBinding(
                InlineResult,
                hasLegacyEndpointUrl,
                hasLegacyHttpMethod,
                hasLegacyAuthHeaders,
                endpointUrlFieldName,
                httpMethodFieldName,
                authHeadersFieldName);
            persistedExecutionKind = InlineResult;
            persistedExecutionBinding = CreateInlineResultBinding(inlineBinding.Output, inlineBinding.Meta);
            persistedEndpointUrl = null;
            persistedHttpMethod = "POST";
            persistedAuthHeaders = null;
        }

        return new PersistedToolExecutionSettings(
            persistedExecutionKind,
            persistedExecutionBinding,
            persistedEndpointUrl,
            persistedHttpMethod,
            persistedAuthHeaders);
    }

    public static PersistedToolExecutionSettings NormalizePersistedExecutionSettingsForStorage(
        string? executionKind,
        string? executionBinding,
        string? endpointUrl,
        string? httpMethod,
        string? authHeaders,
        string executionKindFieldName,
        string executionBindingFieldName,
        string endpointUrlFieldName,
        string httpMethodFieldName,
        string authHeadersFieldName)
    {
        var normalizedLegacyEndpointUrl = string.IsNullOrWhiteSpace(endpointUrl) ? null : endpointUrl.Trim();
        var normalizedLegacyHttpMethod = string.IsNullOrWhiteSpace(httpMethod) ? "POST" : httpMethod.Trim().ToUpperInvariant();
        var normalizedLegacyAuthHeaders = ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(authHeaders, authHeadersFieldName);
        var normalizedExecutionKind = NormalizeExecutionKind(executionKind, executionKindFieldName);
        var normalizedExecutionBinding = ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(executionBinding, executionBindingFieldName);

        if (normalizedExecutionKind is null && normalizedExecutionBinding is not null)
        {
            throw new InvalidOperationException($"{executionKindFieldName} is required when {executionBindingFieldName} is provided.");
        }

        if (normalizedExecutionKind is null && !string.IsNullOrWhiteSpace(normalizedLegacyEndpointUrl))
        {
            return ResolvePersistedExecutionSettings(
                executionKind,
                executionBinding,
                endpointUrl,
                httpMethod,
                authHeaders,
                executionKindFieldName,
                executionBindingFieldName,
                endpointUrlFieldName,
                httpMethodFieldName,
                authHeadersFieldName);
        }

        if (normalizedExecutionKind == Webhook)
        {
            var webhookBinding = ParseWebhookBinding(normalizedExecutionBinding, executionBindingFieldName);
            return new PersistedToolExecutionSettings(
                Webhook,
                CreateWebhookBinding(
                    webhookBinding.EndpointUrl,
                    webhookBinding.HttpMethod,
                    webhookBinding.AuthHeaders),
                webhookBinding.EndpointUrl,
                webhookBinding.HttpMethod,
                webhookBinding.AuthHeaders);
        }

        if (normalizedExecutionKind == LocalHandler)
        {
            var localBinding = ParseLocalHandlerBinding(normalizedExecutionBinding, executionBindingFieldName);
            return new PersistedToolExecutionSettings(
                LocalHandler,
                CreateLocalHandlerBinding(localBinding.HandlerName),
                null,
                "POST",
                null);
        }

        if (normalizedExecutionKind == InlineResult)
        {
            var inlineBinding = ParseInlineResultBinding(normalizedExecutionBinding, executionBindingFieldName);
            return new PersistedToolExecutionSettings(
                InlineResult,
                CreateInlineResultBinding(inlineBinding.Output, inlineBinding.Meta),
                null,
                "POST",
                null);
        }

        return new PersistedToolExecutionSettings(
            normalizedExecutionKind,
            normalizedExecutionBinding,
            normalizedLegacyEndpointUrl,
            normalizedLegacyHttpMethod,
            normalizedLegacyAuthHeaders);
    }

    private static VersionedExecutionBindingDocument? TryParseVersionedBinding(string binding, string fieldName)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<VersionedExecutionBindingDocument>(binding, JsonOptions);
            if (payload is null || payload.Version <= 0 || string.IsNullOrWhiteSpace(payload.Type))
            {
                return null;
            }

            if (payload.Version > CurrentBindingVersion)
            {
                throw new InvalidOperationException($"{fieldName} uses unsupported binding version '{payload.Version}'.");
            }

            return payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static WebhookExecutionBinding NormalizeWebhookBinding(WebhookExecutionBinding? payload, string fieldName)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.EndpointUrl))
        {
            throw new InvalidOperationException($"{fieldName} for '{Webhook}' must include 'endpointUrl'.");
        }

        var endpointUrl = payload.EndpointUrl.Trim();
        var httpMethod = string.IsNullOrWhiteSpace(payload.HttpMethod)
            ? "POST"
            : payload.HttpMethod.Trim().ToUpperInvariant();
        var authHeaders = string.IsNullOrWhiteSpace(payload.AuthHeaders)
            ? null
            : ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(payload.AuthHeaders, fieldName);

        return new WebhookExecutionBinding(endpointUrl, httpMethod, authHeaders);
    }

    private static LocalHandlerExecutionBinding NormalizeLocalHandlerBinding(LocalHandlerExecutionBinding? payload, string fieldName)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.HandlerName))
        {
            throw new InvalidOperationException($"{fieldName} for '{LocalHandler}' must include 'handlerName'.");
        }

        return new LocalHandlerExecutionBinding(payload.HandlerName.Trim());
    }

    private static InlineResultExecutionBinding NormalizeInlineResultBinding(InlineResultExecutionBinding? payload, string fieldName)
    {
        if (payload is null || payload.Output is null)
        {
            throw new InvalidOperationException($"{fieldName} for '{InlineResult}' must include 'output'.");
        }

        var meta = string.IsNullOrWhiteSpace(payload.Meta)
            ? null
            : ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(payload.Meta, fieldName);

        return new InlineResultExecutionBinding(payload.Output, meta);
    }

    private static void EnsureWebhookLegacyFieldsMatchExplicitBinding(
        WebhookExecutionBinding binding,
        string? endpointUrl,
        string httpMethod,
        string? authHeaders,
        bool hasEndpointUrl,
        bool hasHttpMethod,
        bool hasAuthHeaders,
        string executionBindingFieldName,
        string endpointUrlFieldName,
        string httpMethodFieldName,
        string authHeadersFieldName)
    {
        if (hasEndpointUrl && !string.Equals(endpointUrl, binding.EndpointUrl, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{endpointUrlFieldName} must match {executionBindingFieldName} for '{Webhook}' execution kind.");
        }

        if (hasHttpMethod && !string.Equals(httpMethod, binding.HttpMethod, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{httpMethodFieldName} must match {executionBindingFieldName} for '{Webhook}' execution kind.");
        }

        if (hasAuthHeaders && !string.Equals(authHeaders, binding.AuthHeaders, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{authHeadersFieldName} must match {executionBindingFieldName} for '{Webhook}' execution kind.");
        }
    }

    private static void EnsureLegacyWebhookFieldsOmittedForNonWebhookBinding(
        string executionKind,
        bool hasEndpointUrl,
        bool hasHttpMethod,
        bool hasAuthHeaders,
        string endpointUrlFieldName,
        string httpMethodFieldName,
        string authHeadersFieldName)
    {
        if (hasEndpointUrl)
        {
            throw new InvalidOperationException($"{endpointUrlFieldName} must be omitted when execution kind is '{executionKind}'.");
        }

        if (hasHttpMethod)
        {
            throw new InvalidOperationException($"{httpMethodFieldName} must be omitted when execution kind is '{executionKind}'.");
        }

        if (hasAuthHeaders)
        {
            throw new InvalidOperationException($"{authHeadersFieldName} must be omitted when execution kind is '{executionKind}'.");
        }
    }
}

public sealed record VersionedExecutionBindingDocument(
    int Version,
    string Type,
    WebhookExecutionBinding? Webhook,
    LocalHandlerExecutionBinding? LocalHandler,
    InlineResultExecutionBinding? InlineResult);

public sealed record WebhookExecutionBinding(string EndpointUrl, string HttpMethod, string? AuthHeaders);

public sealed record LocalHandlerExecutionBinding(string HandlerName);

public sealed record InlineResultExecutionBinding(string Output, string? Meta);

public sealed record PersistedToolExecutionSettings(
    string? ExecutionKind,
    string? ExecutionBinding,
    string? EndpointUrl,
    string HttpMethod,
    string? AuthHeaders);
