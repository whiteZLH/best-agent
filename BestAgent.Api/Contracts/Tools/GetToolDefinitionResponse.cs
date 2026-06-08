namespace BestAgent.Api.Contracts.Tools;

public record GetToolDefinitionResponse(
    string Id,
    string ToolName,
    string DisplayName,
    string? Description,
    string? InputSchema,
    string? OutputSchema,
    string? ExecutionKind,
    string? ExecutionBinding,
    ToolExecutionResponse? Execution,
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
    ToolPoliciesResponse? Policies = null,
    string? ParameterPolicy = null);

public record ToolExecutionResponse(
    string? Kind,
    string? Binding,
    WebhookToolExecutionResponse? Webhook,
    LocalHandlerToolExecutionResponse? LocalHandler,
    InlineResultToolExecutionResponse? InlineResult);

public record WebhookToolExecutionResponse(
    string EndpointUrl,
    string HttpMethod,
    string? AuthHeaders);

public record LocalHandlerToolExecutionResponse(string HandlerName);

public record InlineResultToolExecutionResponse(string Output, string? Meta);

public record ToolPoliciesResponse(
    RetryToolPolicyResponse? Retry,
    AuthToolPolicyResponse? Auth,
    IdempotencyToolPolicyResponse? Idempotency,
    CompensationToolPolicyResponse? Compensation,
    ParameterToolPolicyResponse? Parameter = null);

public record RetryToolPolicyResponse(
    int? MaxAttempts,
    int? DelayMs);

public record AuthToolPolicyResponse(string? Scheme);

public record ParameterToolPolicyResponse(
    IReadOnlyList<string> AllowedPaths,
    IReadOnlyList<string> DeniedPaths);

public record IdempotencyToolPolicyResponse(bool? Enabled);

public record CompensationToolPolicyResponse(string? Mode);
