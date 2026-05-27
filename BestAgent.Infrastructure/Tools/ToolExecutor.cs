using BestAgent.Application.Tools;

namespace BestAgent.Infrastructure.Tools;

public class ToolExecutor : IToolExecutor
{
    private readonly ToolRegistry _toolRegistry;

    public ToolExecutor(ToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string? input,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!_toolRegistry.TryGet(toolName, out var handler) || handler is null)
        {
            throw new InvalidOperationException($"Tool '{toolName}' is not registered.");
        }

        return await handler(input, context, cancellationToken);
    }
}
