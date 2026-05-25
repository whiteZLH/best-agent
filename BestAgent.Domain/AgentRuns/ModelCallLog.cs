using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentRuns;

public record class ModelCallLog : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string RequestMode { get; init; } = string.Empty;
    public string? RequestPayload { get; init; }
    public string? ResponsePayload { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
    public long LatencyMs { get; init; }
    public decimal CostAmount { get; init; }
    public string FinishReason { get; init; } = string.Empty;
    public bool SuccessFlag { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}
