using BestAgent.Application.Tools;
using BestAgent.Domain.Tools;

namespace BestAgent.Infrastructure.Tools;

public class ToolResolver : IToolResolver
{
    private readonly IToolHandlerRegistry _toolHandlerRegistry;
    private readonly IToolDefinitionRepository _toolDefinitionRepository;

    public ToolResolver(
        IToolHandlerRegistry toolHandlerRegistry,
        IToolDefinitionRepository toolDefinitionRepository)
    {
        _toolHandlerRegistry = toolHandlerRegistry;
        _toolDefinitionRepository = toolDefinitionRepository;
    }

    public async Task<ToolResolution> ResolveAsync(
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
                return new ToolResolution(
                    ToolExecutionKind.Webhook,
                    toolName,
                    definition,
                    null,
                    new HttpToolInvocationRequest(
                        definition.ToolName,
                        definition.EndpointUrl,
                        definition.HttpMethod,
                        definition.AuthHeaders,
                        input,
                        definition.InputSchema,
                        definition.OutputSchema,
                        context,
                        definition.TimeoutMs));
            }

            if (_toolHandlerRegistry.TryGetHandler(toolName, out var fallbackHandler) && fallbackHandler is not null)
            {
                return new ToolResolution(
                    ToolExecutionKind.LocalHandler,
                    toolName,
                    definition,
                    fallbackHandler,
                    null);
            }

            throw new InvalidOperationException($"Tool '{toolName}' is defined but has no endpoint URL configured and no registered handler.");
        }

        if (_toolHandlerRegistry.TryGetHandler(toolName, out var handler) && handler is not null)
        {
            return new ToolResolution(
                ToolExecutionKind.LocalHandler,
                toolName,
                null,
                handler,
                null);
        }

        throw new InvalidOperationException($"Tool '{toolName}' has no tool definition and no registered handler.");
    }
}
