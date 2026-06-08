namespace BestAgent.Api.Contracts.AgentRuns;

public record ModelCallInfoResponse(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    ModelCallRetrievalInfoResponse? Retrieval = null);

public record ModelCallRetrievalInfoResponse(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);
