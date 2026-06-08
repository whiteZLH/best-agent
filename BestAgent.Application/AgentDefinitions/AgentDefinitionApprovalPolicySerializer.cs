using System.Text.Json;
using BestAgent.Application.AgentRuns.Approvals;

namespace BestAgent.Application.AgentDefinitions;

internal static class AgentDefinitionApprovalPolicySerializer
{
    public static string? NormalizeOptional(string? approvalPolicy)
    {
        if (string.IsNullOrWhiteSpace(approvalPolicy))
        {
            return null;
        }

        var parsed = ApprovalPolicyParser.ParseOptional(approvalPolicy);
        if (parsed is null)
        {
            return null;
        }

        var normalized = ApprovalPolicyOptionsNormalizer.Normalize(parsed);
        return JsonSerializer.Serialize(normalized);
    }
}
