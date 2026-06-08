namespace BestAgent.Application.AgentRuns.Commands.RequestHumanAgentRun;

public record RequestHumanAgentRunResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
