namespace BestAgent.Application.Models;

public record GenerateTextResult(
    string Output,
    int PromptTokens = 0,
    int CompletionTokens = 0,
    int TotalTokens = 0,
    decimal Cost = 0m,
    string? FinishReason = null,
    string? ReasoningSummary = null,
    IReadOnlyList<GenerateTextToolCall>? ToolCalls = null);
