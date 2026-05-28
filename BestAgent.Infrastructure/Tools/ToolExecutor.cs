using BestAgent.Application.Tools;
using BestAgent.Domain.Tools;

namespace BestAgent.Infrastructure.Tools;

public class ToolExecutor : IToolExecutor
{
    private readonly ToolRegistry _toolRegistry;
    private readonly IToolDefinitionRepository _toolDefinitionRepository;
    private readonly IHttpToolInvoker _httpToolInvoker;

    public ToolExecutor(
        ToolRegistry toolRegistry,
        IToolDefinitionRepository toolDefinitionRepository,
        IHttpToolInvoker httpToolInvoker)
    {
        _toolRegistry = toolRegistry;
        _toolDefinitionRepository = toolDefinitionRepository;
        _httpToolInvoker = httpToolInvoker;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string? input,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (_toolRegistry.TryGet(toolName, out var handler) && handler is not null)
        {
            return await handler(input, context, cancellationToken);
        }

        var definition = await _toolDefinitionRepository.GetByToolNameAsync(toolName, cancellationToken);
        if (definition is null)
        {
            throw new InvalidOperationException($"Tool '{toolName}' has no registered handler and no tool definition.");
        }

        if (!definition.Enabled)
        {
            throw new InvalidOperationException($"Tool '{toolName}' is disabled.");
        }

        if (string.IsNullOrWhiteSpace(definition.EndpointUrl))
        {
            throw new InvalidOperationException($"Tool '{toolName}' has no registered handler and no endpoint URL configured.");
        }

        var request = new HttpToolInvocationRequest(
            definition.ToolName,
            definition.EndpointUrl,
            definition.HttpMethod,
            definition.AuthHeaders,
            input,
            definition.InputSchema,
            definition.OutputSchema,
            context,
            definition.TimeoutMs);

        return await _httpToolInvoker.InvokeAsync(request, cancellationToken);
    }
}
