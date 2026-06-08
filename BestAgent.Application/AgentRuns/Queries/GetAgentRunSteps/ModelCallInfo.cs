namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record ModelCallInfo(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost);
