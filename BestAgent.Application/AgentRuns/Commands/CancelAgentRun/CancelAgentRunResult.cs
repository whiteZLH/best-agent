namespace BestAgent.Application.AgentRuns.Commands.CancelAgentRun;

public record CancelAgentRunResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null,
    string? Reason = null);
