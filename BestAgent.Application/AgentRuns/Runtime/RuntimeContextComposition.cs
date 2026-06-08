namespace BestAgent.Application.AgentRuns.Runtime;

public sealed record RuntimeContextComposition(
    string ModelInput,
    RuntimeRetrievalAudit? Retrieval = null);

public sealed record RuntimeRetrievalAudit(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);
