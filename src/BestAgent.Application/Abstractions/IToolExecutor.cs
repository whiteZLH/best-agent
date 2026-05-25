using BestAgent.Application.Planning;

namespace BestAgent.Application.Abstractions;

public interface IToolExecutor
{
    Task<ToolExecutionResult> ExecuteAsync(string toolName, string argumentsJson, CancellationToken cancellationToken);
}
