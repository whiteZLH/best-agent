namespace BestAgent.Application.Tools;

public interface IToolResolver
{
    Task<ToolResolution> ResolveAsync(string toolName, string? input, ToolExecutionContext context, CancellationToken cancellationToken);
}
