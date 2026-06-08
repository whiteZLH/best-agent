namespace BestAgent.Api.Contracts.Tools;

public record ToolPoliciesRequest(
    RetryToolPolicyRequest? Retry,
    AuthToolPolicyRequest? Auth,
    IdempotencyToolPolicyRequest? Idempotency,
    CompensationToolPolicyRequest? Compensation,
    ParameterToolPolicyRequest? Parameter = null);

public record RetryToolPolicyRequest(
    int? MaxAttempts,
    int? DelayMs);

public record AuthToolPolicyRequest(string? Scheme);

public record ParameterToolPolicyRequest(
    IReadOnlyList<string>? AllowedPaths,
    IReadOnlyList<string>? DeniedPaths);

public record IdempotencyToolPolicyRequest(bool? Enabled);

public record CompensationToolPolicyRequest(string? Mode);
