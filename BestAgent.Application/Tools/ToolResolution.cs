using BestAgent.Domain.Tools;

namespace BestAgent.Application.Tools;

public enum ToolExecutionKind
{
    LocalHandler,
    Webhook
}

public record ToolResolution(
    ToolExecutionKind ExecutionKind,
    string ToolName,
    ToolDefinition? Definition,
    Func<string?, ToolExecutionContext, CancellationToken, Task<ToolExecutionResult>>? LocalHandler,
    HttpToolInvocationRequest? WebhookRequest);
