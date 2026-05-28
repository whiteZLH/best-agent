namespace BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;

public record ApproveAgentRunStepResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
