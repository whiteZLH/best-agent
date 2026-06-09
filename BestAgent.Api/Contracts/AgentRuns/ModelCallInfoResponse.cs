namespace BestAgent.Api.Contracts.AgentRuns;

public record ModelCallInfoResponse(
    string Model,
    string? ResponseId,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    ModelCallRetrievalInfoResponse? Retrieval = null,
    string? FinishReason = null,
    string? ServiceTier = null,
    string? ReasoningSummary = null,
    IReadOnlyList<ModelCallToolCallInfoResponse>? ToolCalls = null);

public record ModelCallToolCallInfoResponse(
    string Id,
    string Type,
    string Name,
    string? Arguments = null);

public record ModelCallRetrievalInfoResponse(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);
