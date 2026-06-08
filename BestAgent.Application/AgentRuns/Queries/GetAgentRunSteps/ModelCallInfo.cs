namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record ModelCallInfo(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    string? FinishReason = null,
    ModelCallRetrievalInfo? Retrieval = null,
    string? ReasoningSummary = null,
    IReadOnlyList<ModelCallToolCallInfo>? ToolCalls = null);

public record ModelCallToolCallInfo(
    string Id,
    string Type,
    string Name,
    string? Arguments = null);

public record ModelCallRetrievalInfo(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);
