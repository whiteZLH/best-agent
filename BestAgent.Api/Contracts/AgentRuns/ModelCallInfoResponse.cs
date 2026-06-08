namespace BestAgent.Api.Contracts.AgentRuns;

public record ModelCallInfoResponse(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost);
