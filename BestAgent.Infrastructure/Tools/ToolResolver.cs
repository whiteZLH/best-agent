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

            var executionKind = ToolExecutionBindingHelper.NormalizeExecutionKind(definition.ExecutionKind, nameof(definition.ExecutionKind));
            var normalizedAuthPolicy = ToolAuthPolicyHelper.NormalizeOptionalPolicy(
                definition.AuthPolicy,
                nameof(definition.AuthPolicy));
            if (executionKind == ToolExecutionBindingHelper.Webhook)
            {
                var webhookBinding = ToolExecutionBindingHelper.ParseWebhookBinding(definition.ExecutionBinding, nameof(definition.ExecutionBinding));
                ToolAuthPolicyHelper.ValidateExecutionCompatibility(
                    normalizedAuthPolicy,
                    executionKind,
                    webhookBinding.AuthHeaders,
                    nameof(definition.AuthPolicy),
                    nameof(definition.ExecutionKind),
                    nameof(definition.AuthHeaders));
                return ResolveWebhook(toolName, input, context, definition, webhookBinding);
            }

            if (executionKind == ToolExecutionBindingHelper.LocalHandler)
            {
                ToolAuthPolicyHelper.ValidateExecutionCompatibility(
                    normalizedAuthPolicy,
                    executionKind,
                    null,
                    nameof(definition.AuthPolicy),
                    nameof(definition.ExecutionKind),
                    nameof(definition.AuthHeaders));
                var localBinding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(definition.ExecutionBinding, nameof(definition.ExecutionBinding));
                if (_toolHandlerRegistry.TryGetHandler(localBinding.HandlerName, out var localHandler) && localHandler is not null)
                {
                    return new ToolResolution(
                        ToolExecutionKind.LocalHandler,
                        toolName,
                        definition,
                        localHandler,
                        null,
                        null);
                }

                throw new InvalidOperationException($"Tool '{toolName}' is defined with local handler '{localBinding.HandlerName}' but no registered handler exists.");
            }

            if (executionKind == ToolExecutionBindingHelper.InlineResult)
            {
                ToolAuthPolicyHelper.ValidateExecutionCompatibility(
                    normalizedAuthPolicy,
                    executionKind,
                    null,
                    nameof(definition.AuthPolicy),
                    nameof(definition.ExecutionKind),
                    nameof(definition.AuthHeaders));
                var inlineBinding = ToolExecutionBindingHelper.ParseInlineResultBinding(definition.ExecutionBinding, nameof(definition.ExecutionBinding));
                return new ToolResolution(
                    ToolExecutionKind.InlineResult,
                    toolName,
                    definition,
                    null,
                    null,
                    new InlineToolInvocationRequest(toolName, inlineBinding.Output ?? string.Empty, inlineBinding.Meta));
            }

            throw new InvalidOperationException($"Tool '{toolName}' is defined but has no explicit execution binding configured.");
        }

        throw new InvalidOperationException($"Tool '{toolName}' has no persisted tool definition.");
    }

    private static ToolResolution ResolveWebhook(
        string toolName,
        string? input,
        ToolExecutionContext context,
        ToolDefinition definition,
        WebhookExecutionBinding webhookBinding)
    {
        var idempotencyKey = ToolIdempotencyPolicyHelper.IsEnabled(definition.ToolName, definition.IdempotencyPolicy)
            ? Guid.NewGuid().ToString("N")
            : null;

        return new ToolResolution(
            ToolExecutionKind.Webhook,
            toolName,
            definition,
            null,
            new HttpToolInvocationRequest(
                definition.ToolName,
                webhookBinding.EndpointUrl,
                webhookBinding.HttpMethod,
                webhookBinding.AuthHeaders,
                idempotencyKey,
                input,
                definition.InputSchema,
                definition.OutputSchema,
                definition.RetryPolicy,
                context,
                definition.TimeoutMs),
            null);
    }
}
