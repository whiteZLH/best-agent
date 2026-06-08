using System.Text.Json;
using BestAgent.Domain.Tools;

namespace BestAgent.Application.Tools;

public record ToolDefinitionViewModel(
    string Id,
    string ToolName,
    string DisplayName,
    string? Description,
    string? InputSchema,
    string? OutputSchema,
    string? ExecutionKind,
    string? ExecutionBinding,
    ToolExecutionViewModel? Execution,
    string? EndpointUrl,
    string? HttpMethod,
    string? AuthHeaders,
    string? CallbackSecret,
    string SideEffectLevel,
    int TimeoutMs,
    string? RetryPolicy,
    string? AuthPolicy,
    string? IdempotencyPolicy,
    bool AsyncSupported,
    string ConsistencyMode,
    string? CompensationPolicy,
    bool Enabled,
    DateTime CreateTime,
    DateTime LastModifyTime,
    ToolPoliciesViewModel? Policies = null,
    string? ParameterPolicy = null)
{
    public static ToolDefinitionViewModel FromEntity(ToolDefinition entity)
    {
        var executionKind = ToolExecutionBindingHelper.NormalizeExecutionKind(entity.ExecutionKind, nameof(entity.ExecutionKind));
        var executionBinding = ToolSensitiveDataMasker.MaskExecutionBinding(executionKind, entity.ExecutionBinding);
        ToolExecutionViewModel? execution = null;
        int? executionVersion = null;
        var endpointUrl = entity.EndpointUrl;
        string? httpMethod = string.IsNullOrWhiteSpace(entity.HttpMethod) ? null : entity.HttpMethod;
        var authHeaders = ToolSensitiveDataMasker.MaskAuthHeaders(entity.AuthHeaders);

        if (executionKind is null && !string.IsNullOrWhiteSpace(entity.EndpointUrl))
        {
            var webhookBinding = new WebhookExecutionBinding(
                entity.EndpointUrl.Trim(),
                string.IsNullOrWhiteSpace(entity.HttpMethod) ? "POST" : entity.HttpMethod.Trim().ToUpperInvariant(),
                authHeaders);
            executionKind = ToolExecutionBindingHelper.Webhook;
            executionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                webhookBinding.EndpointUrl,
                webhookBinding.HttpMethod,
                webhookBinding.AuthHeaders);
            endpointUrl = webhookBinding.EndpointUrl;
            httpMethod = webhookBinding.HttpMethod;
            execution = new ToolExecutionViewModel(
                executionKind,
                executionBinding,
                new WebhookToolExecutionViewModel(
                    webhookBinding.EndpointUrl,
                    webhookBinding.HttpMethod,
                    webhookBinding.AuthHeaders),
                null,
                null,
                ToolExecutionBindingHelper.CurrentBindingVersion);
        }

        if (execution is null
            && executionKind == ToolExecutionBindingHelper.Webhook
            && !string.IsNullOrWhiteSpace(executionBinding))
        {
            executionVersion = ToolExecutionBindingHelper.ResolveExecutionBindingVersion(executionBinding);
            var webhookBinding = ToolExecutionBindingHelper.ParseWebhookBinding(executionBinding, nameof(entity.ExecutionBinding));
            endpointUrl = webhookBinding.EndpointUrl;
            httpMethod = webhookBinding.HttpMethod;
            authHeaders = webhookBinding.AuthHeaders;
            execution = new ToolExecutionViewModel(
                executionKind,
                executionBinding,
                new WebhookToolExecutionViewModel(
                    webhookBinding.EndpointUrl,
                    webhookBinding.HttpMethod,
                    webhookBinding.AuthHeaders),
                null,
                null,
                executionVersion);
        }
        else if (executionKind == ToolExecutionBindingHelper.LocalHandler && !string.IsNullOrWhiteSpace(executionBinding))
        {
            executionVersion = ToolExecutionBindingHelper.ResolveExecutionBindingVersion(executionBinding);
            var localHandlerBinding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(executionBinding, nameof(entity.ExecutionBinding));
            execution = new ToolExecutionViewModel(
                executionKind,
                executionBinding,
                null,
                new LocalHandlerToolExecutionViewModel(localHandlerBinding.HandlerName),
                null,
                executionVersion);
            endpointUrl = null;
            httpMethod = null;
            authHeaders = null;
        }
        else if (executionKind == ToolExecutionBindingHelper.InlineResult && !string.IsNullOrWhiteSpace(executionBinding))
        {
            executionVersion = ToolExecutionBindingHelper.ResolveExecutionBindingVersion(executionBinding);
            var inlineResultBinding = ToolExecutionBindingHelper.ParseInlineResultBinding(executionBinding, nameof(entity.ExecutionBinding));
            execution = new ToolExecutionViewModel(
                executionKind,
                executionBinding,
                null,
                null,
                new InlineResultToolExecutionViewModel(
                    ToolSensitiveDataMasker.MaskRuntimePayload(inlineResultBinding.Output) ?? inlineResultBinding.Output,
                    ToolSensitiveDataMasker.MaskRuntimePayload(inlineResultBinding.Meta)),
                executionVersion);
            endpointUrl = null;
            httpMethod = null;
            authHeaders = null;
        }

        var normalizedPolicies = ToolPolicySettingsHelper.NormalizePersistedPolicySettings(
            entity.RetryPolicy,
            entity.AuthPolicy,
            entity.ParameterPolicy,
            entity.IdempotencyPolicy,
            entity.CompensationPolicy,
            entity.ConsistencyMode,
            entity.SideEffectLevel,
            nameof(entity.RetryPolicy),
            nameof(entity.AuthPolicy),
            nameof(entity.ParameterPolicy),
            nameof(entity.IdempotencyPolicy),
            nameof(entity.CompensationPolicy),
            nameof(entity.ConsistencyMode),
            nameof(entity.SideEffectLevel));
        var policies = CreatePoliciesViewModel(
            normalizedPolicies.RetryPolicy,
            normalizedPolicies.AuthPolicy,
            normalizedPolicies.ParameterPolicy,
            normalizedPolicies.IdempotencyPolicy,
            normalizedPolicies.CompensationPolicy);
        var maskedAuthPolicy = ToolSensitiveDataMasker.MaskStructuredPolicyPayload(normalizedPolicies.AuthPolicy);
        var normalizedSideEffectLevel = ToolDefinitionPolicyValidator.NormalizeSideEffectLevel(
            entity.SideEffectLevel,
            nameof(entity.SideEffectLevel));

        return new ToolDefinitionViewModel(
            entity.Id,
            entity.ToolName,
            entity.DisplayName,
            entity.Description,
            entity.InputSchema,
            entity.OutputSchema,
            executionKind,
            executionBinding,
            execution,
            endpointUrl,
            httpMethod,
            authHeaders,
            ToolSensitiveDataMasker.MaskOptionalSecret(entity.CallbackSecret),
            normalizedSideEffectLevel,
            entity.TimeoutMs,
            normalizedPolicies.RetryPolicy,
            maskedAuthPolicy,
            normalizedPolicies.IdempotencyPolicy,
            entity.AsyncSupported,
            normalizedPolicies.ConsistencyMode,
            normalizedPolicies.CompensationPolicy,
            entity.Enabled,
            entity.CreateTime,
            entity.LastModifyTime,
            policies,
            normalizedPolicies.ParameterPolicy);
    }

    private static ToolPoliciesViewModel? CreatePoliciesViewModel(
        string? retryPolicy,
        string? authPolicy,
        string? parameterPolicy,
        string? idempotencyPolicy,
        string? compensationPolicy)
    {
        var retry = CreateRetryPolicyViewModel(retryPolicy);
        var auth = CreateNamedPolicyViewModel(authPolicy, "scheme", static scheme => new AuthToolPolicyViewModel(scheme));
        var parameter = CreateParameterPolicyViewModel(parameterPolicy);
        var idempotency = CreateIdempotencyPolicyViewModel(idempotencyPolicy);
        var compensation = CreateNamedPolicyViewModel(
            compensationPolicy,
            "mode",
            static mode => new CompensationToolPolicyViewModel(mode));

        if (retry is null && auth is null && parameter is null && idempotency is null && compensation is null)
        {
            return null;
        }

        return new ToolPoliciesViewModel(retry, auth, idempotency, compensation, parameter);
    }

    private static RetryToolPolicyViewModel? CreateRetryPolicyViewModel(string? retryPolicy)
    {
        if (string.IsNullOrWhiteSpace(retryPolicy))
        {
            return null;
        }

        var normalized = ToolRetryPolicyHelper.NormalizeOptionalPolicy(retryPolicy, nameof(retryPolicy));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        using var document = JsonDocument.Parse(normalized);
        var root = document.RootElement;
        int? maxAttempts = root.TryGetProperty("maxAttempts", out var maxAttemptsProperty)
            && maxAttemptsProperty.ValueKind == JsonValueKind.Number
            && maxAttemptsProperty.TryGetInt32(out var maxAttemptsValue)
                ? maxAttemptsValue
                : null;
        int? delayMs = root.TryGetProperty("delayMs", out var delayMsProperty)
            && delayMsProperty.ValueKind == JsonValueKind.Number
            && delayMsProperty.TryGetInt32(out var delayMsValue)
                ? delayMsValue
                : null;
        return new RetryToolPolicyViewModel(maxAttempts, delayMs);
    }

    private static TPolicy? CreateNamedPolicyViewModel<TPolicy>(
        string? value,
        string propertyName,
        Func<string?, TPolicy> factory)
        where TPolicy : class
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!LooksLikeJson(value))
        {
            return factory(value.Trim());
        }

        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var propertyValue = document.RootElement.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        return factory(propertyValue);
    }

    private static IdempotencyToolPolicyViewModel? CreateIdempotencyPolicyViewModel(string? idempotencyPolicy)
    {
        if (string.IsNullOrWhiteSpace(idempotencyPolicy))
        {
            return null;
        }

        var trimmed = idempotencyPolicy.Trim();
        if (string.Equals(trimmed, "idempotent", StringComparison.OrdinalIgnoreCase))
        {
            return new IdempotencyToolPolicyViewModel(true);
        }

        if (string.Equals(trimmed, "non-idempotent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return new IdempotencyToolPolicyViewModel(false);
        }

        using var document = JsonDocument.Parse(trimmed);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var enabled = document.RootElement.TryGetProperty("enabled", out var property)
            ? property.ValueKind == JsonValueKind.True
            : true;
        return new IdempotencyToolPolicyViewModel(enabled);
    }

    private static ParameterToolPolicyViewModel? CreateParameterPolicyViewModel(string? parameterPolicy)
    {
        var parsed = ToolParameterPolicyHelper.ParseOptional(parameterPolicy);
        return parsed is null
            ? null
            : new ParameterToolPolicyViewModel(parsed.AllowedPaths, parsed.DeniedPaths);
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[') || trimmed.StartsWith('"');
    }
}

public record ToolExecutionViewModel(
    string? Kind,
    string? Binding,
    WebhookToolExecutionViewModel? Webhook,
    LocalHandlerToolExecutionViewModel? LocalHandler,
    InlineResultToolExecutionViewModel? InlineResult,
    int? Version = null);

public record WebhookToolExecutionViewModel(
    string EndpointUrl,
    string HttpMethod,
    string? AuthHeaders);

public record LocalHandlerToolExecutionViewModel(string HandlerName);

public record InlineResultToolExecutionViewModel(string Output, string? Meta);

public record ToolPoliciesViewModel(
    RetryToolPolicyViewModel? Retry,
    AuthToolPolicyViewModel? Auth,
    IdempotencyToolPolicyViewModel? Idempotency,
    CompensationToolPolicyViewModel? Compensation,
    ParameterToolPolicyViewModel? Parameter = null);

public record RetryToolPolicyViewModel(
    int? MaxAttempts,
    int? DelayMs);

public record AuthToolPolicyViewModel(string? Scheme);

public record ParameterToolPolicyViewModel(
    IReadOnlyList<string> AllowedPaths,
    IReadOnlyList<string> DeniedPaths);

public record IdempotencyToolPolicyViewModel(bool? Enabled);

public record CompensationToolPolicyViewModel(string? Mode);
