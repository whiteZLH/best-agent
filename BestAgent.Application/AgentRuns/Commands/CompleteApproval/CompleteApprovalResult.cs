namespace BestAgent.Application.AgentRuns.Commands.CompleteApproval;

public record CompleteApprovalResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string Decision,
    string? WaitToken = null);
