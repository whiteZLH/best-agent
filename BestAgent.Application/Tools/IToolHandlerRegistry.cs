namespace BestAgent.Application.Tools;

public interface IToolHandlerRegistry
{
    bool HasHandler(string toolName);

    bool TryGetHandler(
        string toolName,
        out Func<string?, ToolExecutionContext, CancellationToken, Task<ToolExecutionResult>>? handler);

    IReadOnlyCollection<string> GetRegisteredHandlerNames();
}
