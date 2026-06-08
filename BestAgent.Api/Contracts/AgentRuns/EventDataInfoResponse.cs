namespace BestAgent.Api.Contracts.AgentRuns;

public record EventDataInfoResponse(
    int StepNo,
    string StepType,
    string Status,
    string? Output,
    string? Error,
    EventModelFailureInfoResponse? ModelFailure,
    EventToolFailureInfoResponse? ToolFailure);

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
