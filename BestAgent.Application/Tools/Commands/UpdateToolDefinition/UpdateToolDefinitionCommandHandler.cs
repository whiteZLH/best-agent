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

        var now = DateTime.UtcNow;
        var updated = existing with
        {
            DisplayName = request.DisplayName.Trim(),
            Description = request.Description?.Trim(),
            InputSchema = ToolDefinitionJsonValidator.NormalizeOptionalJson(request.InputSchema, nameof(request.InputSchema)),
            OutputSchema = ToolDefinitionJsonValidator.NormalizeOptionalJson(request.OutputSchema, nameof(request.OutputSchema)),
            EndpointUrl = string.IsNullOrWhiteSpace(request.EndpointUrl) ? null : request.EndpointUrl.Trim(),
            HttpMethod = string.IsNullOrWhiteSpace(request.HttpMethod) ? "POST" : request.HttpMethod.Trim().ToUpperInvariant(),
            AuthHeaders = ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(request.AuthHeaders, nameof(request.AuthHeaders)),
            SideEffectLevel = request.SideEffectLevel.Trim(),
            TimeoutMs = request.TimeoutMs,
            RetryPolicy = request.RetryPolicy,
            AuthPolicy = request.AuthPolicy,
            IdempotencyPolicy = request.IdempotencyPolicy,
            AsyncSupported = request.AsyncSupported,
            ConsistencyMode = request.ConsistencyMode.Trim(),
            CompensationPolicy = request.CompensationPolicy,
            Enabled = request.Enabled,
            LastModifier = "system",
            LastModifierName = "system",
            LastModifyTime = now
        };

        await _toolDefinitionRepository.UpdateAsync(updated, cancellationToken);
        return ToolDefinitionViewModel.FromEntity(updated);
    }
}
