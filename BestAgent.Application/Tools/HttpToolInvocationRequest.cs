namespace BestAgent.Application.Tools;

public record HttpToolInvocationRequest(
    string ToolName,
    string EndpointUrl,
    string HttpMethod,
    string? AuthHeaders,
    string? Input,
    string? InputSchema,
    string? OutputSchema,
    ToolExecutionContext Context,
    int TimeoutMs);
