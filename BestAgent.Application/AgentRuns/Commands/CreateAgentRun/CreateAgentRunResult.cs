namespace BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

public record CreateAgentRunResult(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status);
