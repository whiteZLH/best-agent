namespace BestAgent.Application.AgentRuns.Approvals;

public static class TenantApprovalPolicyOptionsNormalizer
{
    public static TenantApprovalPolicyOptions Normalize(TenantApprovalPolicyOptions? options)
    {
        var normalized = new Dictionary<string, ApprovalPolicyOptions>(StringComparer.OrdinalIgnoreCase);
        if (options?.PoliciesByTenantId is null || options.PoliciesByTenantId.Count == 0)
        {
            return new TenantApprovalPolicyOptions
            {
                PoliciesByTenantId = normalized
            };
        }

        foreach (var pair in options.PoliciesByTenantId)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var tenantId = pair.Key.Trim();
            var policy = ApprovalPolicyOptionsNormalizer.Normalize(pair.Value);
            if (normalized.TryGetValue(tenantId, out var existing))
            {
                normalized[tenantId] = ApprovalPolicyInheritance.MergeStricter(existing, policy);
                continue;
            }

            normalized[tenantId] = policy;
        }

        return new TenantApprovalPolicyOptions
        {
            PoliciesByTenantId = normalized
        };
    }
}
