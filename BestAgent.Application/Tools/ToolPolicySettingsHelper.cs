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
        string sideEffectLevelFieldName)
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
        var normalizedAuthPolicy = ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
            authPolicy,
            authPolicyFieldName,
            "scheme");
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
