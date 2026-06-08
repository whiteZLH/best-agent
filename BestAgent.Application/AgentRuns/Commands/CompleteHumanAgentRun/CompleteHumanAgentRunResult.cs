namespace BestAgent.Application.AgentRuns.Commands.CompleteHumanAgentRun;

public record CompleteHumanAgentRunResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
