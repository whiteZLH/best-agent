namespace BestAgent.Contracts.AgentRuns;

public sealed class CreateAgentRunRequest
{
    public string AgentCode { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public AgentRunInputRequest Input { get; set; } = new();
}
