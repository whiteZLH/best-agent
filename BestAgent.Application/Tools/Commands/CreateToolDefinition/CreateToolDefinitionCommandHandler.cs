using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.Tools.Commands.CreateToolDefinition;

public class CreateToolDefinitionCommandHandler : IRequestHandler<CreateToolDefinitionCommand, ToolDefinitionViewModel>
{
    private readonly IToolDefinitionRepository _toolDefinitionRepository;
    private readonly IToolHandlerRegistry _toolHandlerRegistry;

    public CreateToolDefinitionCommandHandler(
        IToolDefinitionRepository toolDefinitionRepository,
        IToolHandlerRegistry toolHandlerRegistry)
    {
        _toolDefinitionRepository = toolDefinitionRepository;
        _toolHandlerRegistry = toolHandlerRegistry;
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

        var now = DateTime.UtcNow;
        var entity = new ToolDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
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
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        await _toolDefinitionRepository.AddAsync(entity, cancellationToken);
        return ToolDefinitionViewModel.FromEntity(entity, _toolHandlerRegistry.HasHandler(entity.ToolName));
    }
}
