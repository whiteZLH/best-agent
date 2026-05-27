namespace BestAgent.Api.Contracts.AgentRuns;

public record ResumeAgentRunResponse(
    string RunId,
    string AgentCode,
    string? Input,
    string? Output,
    string Status,
    string? WaitToken = null);
