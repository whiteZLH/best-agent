namespace BestAgent.Api.Contracts.AgentRuns;

public record RejectAgentRunStepResponse(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
