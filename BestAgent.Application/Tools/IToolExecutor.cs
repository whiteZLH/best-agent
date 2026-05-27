namespace BestAgent.Application.Tools;

public interface IToolExecutor
{
    Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string? input,
        ToolExecutionContext context,
        CancellationToken cancellationToken);
}
