using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.Tools.Commands.CreateToolDefinition;

public class CreateToolDefinitionCommandHandler : IRequestHandler<CreateToolDefinitionCommand, ToolDefinitionViewModel>
{
    private readonly IToolDefinitionRepository _toolDefinitionRepository;

    public CreateToolDefinitionCommandHandler(
        IToolDefinitionRepository toolDefinitionRepository)
    {
        _toolDefinitionRepository = toolDefinitionRepository;
    }

    public async Task<ToolDefinitionViewModel> Handle(CreateToolDefinitionCommand request, CancellationToken cancellationToken)
    {
        var toolName = request.ToolName.Trim();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new InvalidOperationException("Tool name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new InvalidOperationException("Display name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SideEffectLevel))
        {
            throw new InvalidOperationException("Side effect level is required.");
        }

        if (request.TimeoutMs <= 0)
        {
            throw new InvalidOperationException("Timeout must be greater than zero.");
        }

        if (await _toolDefinitionRepository.ExistsByToolNameAsync(toolName, cancellationToken))
        {
            throw new InvalidOperationException($"Tool name '{toolName}' already exists.");
        }

        var executionSettings = ToolExecutionBindingHelper.ResolvePersistedExecutionSettings(
            request.ExecutionKind,
            request.ExecutionBinding,
            request.EndpointUrl,
            request.HttpMethod,
            request.AuthHeaders,
            nameof(request.ExecutionKind),
            nameof(request.ExecutionBinding),
            nameof(request.EndpointUrl),
            nameof(request.HttpMethod),
            nameof(request.AuthHeaders));
        var policySettings = ToolPolicySettingsHelper.NormalizePersistedPolicySettings(
            request.RetryPolicy,
            request.AuthPolicy,
            request.ParameterPolicy,
            request.IdempotencyPolicy,
            request.CompensationPolicy,
            request.ConsistencyMode,
            request.SideEffectLevel,
            nameof(request.RetryPolicy),
            nameof(request.AuthPolicy),
            nameof(request.ParameterPolicy),
            nameof(request.IdempotencyPolicy),
            nameof(request.CompensationPolicy),
            nameof(request.ConsistencyMode),
            nameof(request.SideEffectLevel));

        var now = DateTime.UtcNow;
        var entity = new ToolDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
            DisplayName = request.DisplayName.Trim(),
            Description = request.Description?.Trim(),
            InputSchema = ToolDefinitionJsonValidator.NormalizeOptionalJson(request.InputSchema, nameof(request.InputSchema)),
            OutputSchema = ToolDefinitionJsonValidator.NormalizeOptionalJson(request.OutputSchema, nameof(request.OutputSchema)),
            ExecutionKind = executionSettings.ExecutionKind,
            ExecutionBinding = executionSettings.ExecutionBinding,
            EndpointUrl = executionSettings.EndpointUrl,
            HttpMethod = executionSettings.HttpMethod,
            AuthHeaders = executionSettings.AuthHeaders,
            CallbackSecret = string.IsNullOrWhiteSpace(request.CallbackSecret) ? null : request.CallbackSecret.Trim(),
            SideEffectLevel = policySettings.SideEffectLevel,
            TimeoutMs = request.TimeoutMs,
            RetryPolicy = policySettings.RetryPolicy,
            AuthPolicy = policySettings.AuthPolicy,
            ParameterPolicy = policySettings.ParameterPolicy,
            IdempotencyPolicy = policySettings.IdempotencyPolicy,
            AsyncSupported = request.AsyncSupported,
            ConsistencyMode = policySettings.ConsistencyMode,
            CompensationPolicy = policySettings.CompensationPolicy,
            Enabled = request.Enabled,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        await _toolDefinitionRepository.AddAsync(entity, cancellationToken);
        return ToolDefinitionViewModel.FromEntity(entity);
    }
}
