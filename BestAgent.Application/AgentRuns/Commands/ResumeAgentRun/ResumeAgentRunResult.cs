namespace BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;

public record ResumeAgentRunResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
