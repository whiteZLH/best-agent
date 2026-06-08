using BestAgent.Domain.Tools;

namespace BestAgent.Application.Tools;

public enum ToolExecutionKind
{
    LocalHandler,
    Webhook,
    InlineResult
}

public record ToolResolution(
    ToolExecutionKind ExecutionKind,
    string ToolName,
    ToolDefinition? Definition,
    Func<string?, ToolExecutionContext, CancellationToken, Task<ToolExecutionResult>>? LocalHandler,
    HttpToolInvocationRequest? WebhookRequest,
    InlineToolInvocationRequest? InlineResultRequest);

public sealed record InlineToolInvocationRequest(
    string ToolName,
    string Output,
    string? Meta);
