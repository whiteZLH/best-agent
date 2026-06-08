namespace BestAgent.Application.Tools;

public static class ToolPolicySettingsHelper
{
    public static PersistedToolPolicySettings NormalizePersistedPolicySettings(
        string? retryPolicy,
        string? authPolicy,
        string? parameterPolicy,
        string? idempotencyPolicy,
        string? compensationPolicy,
        string? consistencyMode,
        string? sideEffectLevel,
        string retryPolicyFieldName,
        string authPolicyFieldName,
        string parameterPolicyFieldName,
        string idempotencyPolicyFieldName,
        string compensationPolicyFieldName,
        string consistencyModeFieldName,
        string sideEffectLevelFieldName,
        string? executionKind = null,
        string? authHeaders = null,
        string executionKindFieldName = "ExecutionKind",
        string authHeadersFieldName = "AuthHeaders")
    {
        var normalizedSideEffectLevel = ToolDefinitionPolicyValidator.NormalizeSideEffectLevel(
            sideEffectLevel,
            sideEffectLevelFieldName);
        var normalizedConsistencyMode = ToolDefinitionPolicyValidator.NormalizeConsistencyMode(
            consistencyMode,
            consistencyModeFieldName);
        var normalizedRetryPolicy = ToolRetryPolicyHelper.NormalizeOptionalPolicy(
            retryPolicy,
            retryPolicyFieldName);
        var normalizedAuthPolicy = ToolAuthPolicyHelper.NormalizeOptionalPolicy(
            authPolicy,
            authPolicyFieldName);
        var normalizedParameterPolicy = ToolParameterPolicyHelper.NormalizeOptionalPolicy(
            parameterPolicy,
            parameterPolicyFieldName);
        var normalizedIdempotencyPolicy = ToolIdempotencyPolicyHelper.NormalizeOptionalPolicy(
            idempotencyPolicy,
            idempotencyPolicyFieldName);
        var normalizedCompensationPolicy = ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
            compensationPolicy,
            compensationPolicyFieldName,
            "mode");

        ToolDefinitionPolicyValidator.ValidateCompensationPolicyRequirement(
            normalizedSideEffectLevel,
            normalizedCompensationPolicy,
            compensationPolicyFieldName);
        ToolAuthPolicyHelper.ValidateExecutionCompatibility(
            normalizedAuthPolicy,
            executionKind,
            authHeaders,
            authPolicyFieldName,
            executionKindFieldName,
            authHeadersFieldName);

        return new PersistedToolPolicySettings(
            normalizedRetryPolicy,
            normalizedAuthPolicy,
            normalizedParameterPolicy,
            normalizedIdempotencyPolicy,
            normalizedCompensationPolicy,
            normalizedConsistencyMode,
            normalizedSideEffectLevel);
    }
}

public sealed record PersistedToolPolicySettings(
    string? RetryPolicy,
    string? AuthPolicy,
    string? ParameterPolicy,
    string? IdempotencyPolicy,
    string? CompensationPolicy,
    string ConsistencyMode,
    string SideEffectLevel);
