namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record ModelCallInfo(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    string? FinishReason = null,
    ModelCallRetrievalInfo? Retrieval = null);

public record ModelCallRetrievalInfo(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);
