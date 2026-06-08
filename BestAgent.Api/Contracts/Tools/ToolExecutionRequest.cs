namespace BestAgent.Api.Contracts.Tools;

public record ToolExecutionRequest(
    string? Kind,
    WebhookToolExecutionRequest? Webhook,
    LocalHandlerToolExecutionRequest? LocalHandler,
    InlineResultToolExecutionRequest? InlineResult);

public record WebhookToolExecutionRequest(
    string EndpointUrl,
    string? HttpMethod,
    string? AuthHeaders);

public record LocalHandlerToolExecutionRequest(string HandlerName);

public record InlineResultToolExecutionRequest(
    string Output,
    string? Meta);
