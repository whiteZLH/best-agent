namespace BestAgent.Api.Contracts.AgentRuns;

public record EventDataInfoResponse(
    int StepNo,
    string StepType,
    string Status,
    string? Output,
    string? Error,
    EventModelCallInfoResponse? ModelCall,
    EventRetrievalInfoResponse? Retrieval,
    EventModelFailureInfoResponse? ModelFailure,
    EventToolFailureInfoResponse? ToolFailure,
    EventToolInvocationInfoResponse? ToolInvocation = null,
    EventApprovalInfoResponse? Approval = null,
    EventHandoffInfoResponse? Handoff = null,
    EventHumanWaitInfoResponse? HumanWait = null);

public record EventModelCallInfoResponse(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    EventModelCallRetrievalInfoResponse? Retrieval,
    string? FinishReason = null,
    string? ReasoningSummary = null);

public record EventModelCallRetrievalInfoResponse(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);

public record EventRetrievalInfoResponse(
    string QueryText);

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

public record EventToolInvocationInfoResponse(
    string InvocationId,
    string ToolName,
    string Mode,
    string Status,
    string CallbackToken);

public record EventApprovalInfoResponse(
    string WaitType,
    string RequestedAction,
    string? RequestPayload,
    string SideEffectLevel,
    string Decision,
    string? Comment,
    DateTime? DecidedAt)
{
    public string ToolName => RequestedAction;

    public string? ToolInput => RequestPayload;
}

public record EventHandoffInfoResponse(
    string WaitType,
    string TargetAgent,
    string? HandoffInput,
    string Mode,
    string ChildRunId,
    string Decision,
    string? ChildStatus,
    string? ChildOutput,
    string? Comment,
    DateTime? DecidedAt,
    string? RouteRuleId,
    string? ContextScope,
    string? MemoryScope,
    string? ToolScope,
    string? KnowledgeScope,
    bool ApprovalRequired,
    string? Reason,
    double? Confidence,
    string? ContextOverrides,
    string? MemoryOverrides,
    string? ToolOverrides,
    string? KnowledgeOverrides,
    string? MergeStrategy = null);

public record EventHumanWaitInfoResponse(
    string WaitType,
    string Decision,
    string? Comment,
    DateTime? DecidedAt,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole,
    string? HumanResult,
    string? SourceType = null,
    string? SourceStepId = null,
    string? SourceInvocationId = null,
    string? SourceToolName = null,
    string? SourceToolInput = null,
    string? SourceToolOutput = null,
    string? SourceToolStatus = null,
    bool ContinueAsToolResult = false);
