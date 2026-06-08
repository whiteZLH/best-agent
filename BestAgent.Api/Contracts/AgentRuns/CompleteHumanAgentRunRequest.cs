namespace BestAgent.Api.Contracts.AgentRuns;

public record CompleteHumanAgentRunRequest(
    string WaitToken,
    string? HumanResult,
    string? Comment,
    bool Terminate,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole);
