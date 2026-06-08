namespace BestAgent.Api.Contracts.AgentRuns;

public record RequestHumanAgentRunRequest(
    string? Comment,
    string? SourceStepId,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole);
