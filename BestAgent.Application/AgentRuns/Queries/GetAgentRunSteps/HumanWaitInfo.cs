namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record HumanWaitInfo(
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
