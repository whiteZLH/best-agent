namespace BestAgent.Application.Tools;

public interface IHttpToolInvoker
{
    Task<ToolExecutionResult> InvokeAsync(
        HttpToolInvocationRequest request,
        CancellationToken cancellationToken);
}
