namespace BestAgent.Application.AgentRuns.Approvals;

public sealed class TenantApprovalPolicyOptions
{
    public IReadOnlyDictionary<string, ApprovalPolicyOptions> PoliciesByTenantId { get; init; }
        = new Dictionary<string, ApprovalPolicyOptions>(StringComparer.OrdinalIgnoreCase);
}
