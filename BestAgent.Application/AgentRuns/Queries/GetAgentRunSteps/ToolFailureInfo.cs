namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record ToolFailureInfo(
    string ToolName,
    string Stage,
    string Message,
    ToolFailureCompensationInfo? Compensation);

public record ToolFailureCompensationInfo(
    string Mode);
