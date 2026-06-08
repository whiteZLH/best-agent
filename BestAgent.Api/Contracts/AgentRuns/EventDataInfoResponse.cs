namespace BestAgent.Api.Contracts.AgentRuns;

public record EventDataInfoResponse(
    int StepNo,
    string StepType,
    string Status,
    string? Output,
    string? Error,
    EventModelCallInfoResponse? ModelCall,
    EventModelFailureInfoResponse? ModelFailure,
    EventToolFailureInfoResponse? ToolFailure);

public record EventModelCallInfoResponse(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    EventModelCallRetrievalInfoResponse? Retrieval);

public record EventModelCallRetrievalInfoResponse(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);

public record EventModelFailureInfoResponse(
    string? ErrorCode,
    string Message);

public record EventToolFailureInfoResponse(
    string ToolName,
    string Stage,
    string Message,
    EventToolFailureCompensationInfoResponse? Compensation);

public record EventToolFailureCompensationInfoResponse(
    string Mode);
