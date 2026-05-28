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
        var definition = await _toolDefinitionRepository.GetByToolNameAsync(toolName, cancellationToken);
        if (definition is not null)
        {
            if (!definition.Enabled)
            {
                throw new InvalidOperationException($"Tool '{toolName}' is disabled.");
            }

            if (!string.IsNullOrWhiteSpace(definition.EndpointUrl))
            {
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

            if (_toolRegistry.TryGet(toolName, out var fallbackHandler) && fallbackHandler is not null)
            {
                return await fallbackHandler(input, context, cancellationToken);
            }

            throw new InvalidOperationException($"Tool '{toolName}' is defined but has no endpoint URL configured and no registered handler.");
        }

        if (_toolRegistry.TryGet(toolName, out var handler) && handler is not null)
        {
            return await handler(input, context, cancellationToken);
        }

        throw new InvalidOperationException($"Tool '{toolName}' has no tool definition and no registered handler.");
    }
}
