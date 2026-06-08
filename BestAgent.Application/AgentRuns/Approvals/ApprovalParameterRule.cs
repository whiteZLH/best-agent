namespace BestAgent.Application.AgentRuns.Approvals;

public sealed class ApprovalParameterRule
{
    public string ToolName { get; init; } = string.Empty;

    public string InputPath { get; init; } = string.Empty;

    public string? ExpectedValue { get; init; }

    public string? OverrideSideEffectLevel { get; init; }
}
