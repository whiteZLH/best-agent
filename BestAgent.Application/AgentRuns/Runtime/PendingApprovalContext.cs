using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Runtime;

public sealed record PendingApprovalContext(
    string StepType,
    string RequestedAction,
    string? RequestPayload,
    string SideEffectLevel);

public static class PendingApprovalContextParser
{
    public static bool TryParsePending(AgentStep step, out PendingApprovalContext? context)
    {
        context = null;

        if (ApprovalPayloadSerializer.TryParse(step.DecisionPayload, out var approvalPayload)
            && string.Equals(approvalPayload!.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
        {
            context = new PendingApprovalContext(
                step.StepType,
                approvalPayload.ToolName,
                approvalPayload.ToolInput,
                approvalPayload.SideEffectLevel);
            return true;
        }

        if (!string.Equals(step.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
            || !HandoffPayloadSerializer.TryParse(step.DecisionPayload, out var handoffPayload)
            || !handoffPayload!.ApprovalRequired
            || !string.Equals(handoffPayload.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        context = new PendingApprovalContext(
            "handoff",
            handoffPayload.TargetAgent,
            handoffPayload.HandoffInput,
            HandoffApprovalDefaults.SideEffectLevel);
        return true;
    }
}
