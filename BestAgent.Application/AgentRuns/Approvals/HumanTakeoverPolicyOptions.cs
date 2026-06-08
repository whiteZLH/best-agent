namespace BestAgent.Application.AgentRuns.Approvals;

public sealed class HumanTakeoverPolicyOptions
{
    public IReadOnlyList<string> AllowedHumanOperatorRoles { get; init; } = [];
}
