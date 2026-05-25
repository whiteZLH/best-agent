namespace BestAgent.Contracts.AgentRuns;

public sealed class AgentRunResponse
{
    public string RunId { get; set; } = string.Empty;

    public string AgentCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Output { get; set; }

    public string? ErrorMessage { get; set; }

    public int CurrentStepNo { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}
