namespace BestAgent.Api.Contracts.AgentRuns;

public record CompleteToolInvocationRequest(
    string WaitToken,
    string ToolResult);
