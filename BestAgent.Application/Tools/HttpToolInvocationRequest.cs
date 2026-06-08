namespace BestAgent.Application.Tools;

public record HttpToolInvocationRequest(
    string ToolName,
    string EndpointUrl,
    string HttpMethod,
    string? AuthHeaders,
    string? IdempotencyKey,
    string? Input,
    string? InputSchema,
    string? OutputSchema,
    string? RetryPolicy,
    ToolExecutionContext Context,
    int TimeoutMs);
