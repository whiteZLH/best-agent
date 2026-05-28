using BestAgent.Domain.Tools;

namespace BestAgent.Application.Tools;

public record ToolDefinitionViewModel(
    string Id,
    string ToolName,
    string DisplayName,
    string? Description,
    string? InputSchema,
    string? OutputSchema,
    string? EndpointUrl,
    string HttpMethod,
    string? AuthHeaders,
    string SideEffectLevel,
    int TimeoutMs,
    string? RetryPolicy,
    string? AuthPolicy,
    string? IdempotencyPolicy,
    bool AsyncSupported,
    string ConsistencyMode,
    string? CompensationPolicy,
    bool Enabled,
    bool HasHandler,
    DateTime CreateTime,
    DateTime LastModifyTime)
{
    public static ToolDefinitionViewModel FromEntity(ToolDefinition entity, bool hasHandler)
    {
        return new ToolDefinitionViewModel(
            entity.Id,
            entity.ToolName,
            entity.DisplayName,
            entity.Description,
            entity.InputSchema,
            entity.OutputSchema,
            entity.EndpointUrl,
            entity.HttpMethod,
            entity.AuthHeaders,
            entity.SideEffectLevel,
            entity.TimeoutMs,
            entity.RetryPolicy,
            entity.AuthPolicy,
            entity.IdempotencyPolicy,
            entity.AsyncSupported,
            entity.ConsistencyMode,
            entity.CompensationPolicy,
            entity.Enabled,
            hasHandler,
            entity.CreateTime,
            entity.LastModifyTime);
    }
}
