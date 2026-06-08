using BestAgent.Application.Exceptions;
using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.Tools.Commands.UpdateToolDefinition;

public class UpdateToolDefinitionCommandHandler : IRequestHandler<UpdateToolDefinitionCommand, ToolDefinitionViewModel>
{
    private readonly IToolDefinitionRepository _toolDefinitionRepository;

    public UpdateToolDefinitionCommandHandler(
        IToolDefinitionRepository toolDefinitionRepository)
    {
        _toolDefinitionRepository = toolDefinitionRepository;
    }

    public async Task<ToolDefinitionViewModel> Handle(UpdateToolDefinitionCommand request, CancellationToken cancellationToken)
    {
        var existing = await _toolDefinitionRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existing is null)
        {
            throw new NotFoundException("ToolDefinition", request.Id);
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
            nameof(request.SideEffectLevel),
            executionSettings.ExecutionKind,
            executionSettings.AuthHeaders,
            nameof(request.ExecutionKind),
            nameof(request.AuthHeaders));

        var now = DateTime.UtcNow;
        var updated = existing with
        {
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
            LastModifier = "system",
            LastModifierName = "system",
            LastModifyTime = now
        };

        await _toolDefinitionRepository.UpdateAsync(updated, cancellationToken);
        return ToolDefinitionViewModel.FromEntity(updated);
    }
}
