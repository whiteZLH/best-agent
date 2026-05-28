namespace BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;

public record RejectAgentRunStepResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
