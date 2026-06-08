namespace BestAgent.Api.Contracts.AgentRuns;

public record ToolFailureInfoResponse(
    string ToolName,
    string Stage,
    string Message,
    ToolFailureCompensationInfoResponse? Compensation);

public record ToolFailureCompensationInfoResponse(
    string Mode);
