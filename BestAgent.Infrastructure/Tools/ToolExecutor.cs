using BestAgent.Application.Tools;

namespace BestAgent.Infrastructure.Tools;

public class ToolExecutor : IToolExecutor
{
    private readonly IToolResolver _toolResolver;
    private readonly IHttpToolInvoker _httpToolInvoker;

    public ToolExecutor(
        IToolResolver toolResolver,
        IHttpToolInvoker httpToolInvoker)
    {
        _toolResolver = toolResolver;
        _httpToolInvoker = httpToolInvoker;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string? input,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var resolution = await _toolResolver.ResolveAsync(toolName, input, context, cancellationToken);

        return resolution.ExecutionKind switch
        {
            ToolExecutionKind.Webhook when resolution.WebhookRequest is not null
                => await _httpToolInvoker.InvokeAsync(resolution.WebhookRequest, cancellationToken),
            ToolExecutionKind.LocalHandler when resolution.LocalHandler is not null
                => await resolution.LocalHandler(input, context, cancellationToken),
            _ => throw new InvalidOperationException($"Tool '{toolName}' resolved to an invalid execution binding.")
        };
    }
}
