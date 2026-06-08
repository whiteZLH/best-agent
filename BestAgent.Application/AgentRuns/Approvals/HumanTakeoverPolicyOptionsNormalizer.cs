namespace BestAgent.Application.AgentRuns.Approvals;

public static class HumanTakeoverPolicyOptionsNormalizer
{
    public static HumanTakeoverPolicyOptions Normalize(HumanTakeoverPolicyOptions? options)
    {
        var source = options ?? new HumanTakeoverPolicyOptions();
        return new HumanTakeoverPolicyOptions
        {
            AllowedHumanOperatorRoles = NormalizeRoles(source.AllowedHumanOperatorRoles)
        };
    }

    private static string[] NormalizeRoles(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var role = value.Trim();
            if (normalized.Any(existing => string.Equals(existing, role, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalized.Add(role);
        }

        return normalized.ToArray();
    }
}
