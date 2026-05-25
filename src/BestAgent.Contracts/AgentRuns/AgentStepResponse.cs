namespace BestAgent.Contracts.AgentRuns;

public sealed class AgentStepResponse
{
    public string StepId { get; set; } = string.Empty;

    public int StepNo { get; set; }

    public string StepType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string InputPayload { get; set; } = string.Empty;

    public string? OutputPayload { get; set; }

    public string? ErrorPayload { get; set; }
}
