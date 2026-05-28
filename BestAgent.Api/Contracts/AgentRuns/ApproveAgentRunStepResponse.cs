namespace BestAgent.Api.Contracts.AgentRuns;

public record ApproveAgentRunStepResponse(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
