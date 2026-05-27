using MediatR;

namespace BestAgent.Application.Tools.Commands.UpdateToolDefinition;

public record UpdateToolDefinitionCommand(
    string Id,
    string DisplayName,
    string? Description,
    string? InputSchema,
    string? OutputSchema,
    string SideEffectLevel,
    int TimeoutMs,
    string? RetryPolicy,
    string? AuthPolicy,
    string? IdempotencyPolicy,
    bool AsyncSupported,
    string ConsistencyMode,
    string? CompensationPolicy,
    bool Enabled) : IRequest<ToolDefinitionViewModel>;
