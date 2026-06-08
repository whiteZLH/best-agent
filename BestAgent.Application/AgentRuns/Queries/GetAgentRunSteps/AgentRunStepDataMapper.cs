using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

internal static class AgentRunStepDataMapper
{
    public static ToolInvocationInfo? MapToolInvocation(ToolInvocation? invocation)
    {
        if (invocation is null)
        {
            return null;
        }

        return new ToolInvocationInfo(
            invocation.InvocationId,
            invocation.ToolName,
            invocation.Mode,
            invocation.Status,
            invocation.CallbackToken,
            invocation.StartedAt,
            invocation.EndedAt,
            invocation.DurationMs);
    }

    public static ApprovalInfo? MapApproval(AgentApproval? approval, string? decisionPayload)
    {
        if (approval is not null)
        {
            return new ApprovalInfo(
                "approval",
                approval.RequestedAction,
                RuntimePayloadMasker.MaskToolInput(approval.RequestPayload),
                approval.RiskLevel,
                approval.Decision,
                string.IsNullOrWhiteSpace(approval.Comment) ? null : approval.Comment,
                approval.DecidedAt,
                approval.ApprovalId,
                string.IsNullOrWhiteSpace(approval.ApproverId) ? null : approval.ApproverId,
                string.IsNullOrWhiteSpace(approval.ApproverName) ? null : approval.ApproverName,
                string.IsNullOrWhiteSpace(approval.ApproverRole) ? null : approval.ApproverRole);
        }

        if (!ApprovalPayloadSerializer.TryParse(decisionPayload, out var payload))
        {
            return null;
        }

        return new ApprovalInfo(
            payload!.WaitType,
            payload.ToolName,
            RuntimePayloadMasker.MaskToolInput(payload.ToolInput),
            payload.SideEffectLevel,
            payload.Decision,
            payload.Comment,
            payload.DecidedAt);
    }

    public static HandoffInfo? MapHandoff(string? decisionPayload)
    {
        if (!HandoffPayloadSerializer.TryParse(decisionPayload, out var payload))
        {
            return null;
        }

        return new HandoffInfo(
            payload!.WaitType,
            payload.TargetAgent,
            payload.HandoffInput,
            payload.Mode,
            payload.ChildRunId,
            payload.Decision,
            payload.ChildStatus,
            payload.ChildOutput,
            payload.Comment,
            payload.DecidedAt,
            payload.RouteRuleId,
            payload.ContextScope,
            payload.MemoryScope,
            payload.ToolScope,
            payload.KnowledgeScope,
            payload.ApprovalRequired,
            payload.Reason,
            payload.Confidence,
            payload.ContextOverrides,
            payload.MemoryOverrides,
            payload.ToolOverrides,
            payload.KnowledgeOverrides,
            payload.MergeStrategy);
    }

    public static HumanWaitInfo? MapHumanWait(string? decisionPayload)
    {
        if (!HumanApprovalPayloadSerializer.TryParse(decisionPayload, out var payload))
        {
            return null;
        }

        return new HumanWaitInfo(
            payload!.WaitType,
            payload.Decision,
            payload.Comment,
            payload.DecidedAt,
            payload.HumanOperatorId,
            payload.HumanOperatorName,
            payload.HumanOperatorRole,
            payload.HumanResult,
            payload.SourceType,
            payload.SourceStepId,
            payload.SourceInvocationId,
            payload.SourceToolName,
            RuntimePayloadMasker.MaskToolInput(payload.SourceToolInput),
            RuntimePayloadMasker.MaskToolOutput(payload.SourceToolOutput),
            payload.SourceToolStatus,
            payload.ContinueAsToolResult);
    }
}
