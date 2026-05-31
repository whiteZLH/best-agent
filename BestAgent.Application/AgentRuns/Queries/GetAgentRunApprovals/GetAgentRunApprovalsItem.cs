using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;

public record GetAgentRunApprovalsItem(
    string ApprovalId,
    string RunId,
    string StepId,
    string RequestedAction,
    string RiskLevel,
    string? RequestPayload,
    string Decision,
    string ApproverId,
    string ApproverRole,
    string ApproverName,
    string Comment,
    string WaitToken,
    DateTime? ExpiresAt,
    DateTime? DecidedAt,
    DateTime CreateTime,
    DateTime LastModifyTime)
{
    public static GetAgentRunApprovalsItem FromEntity(AgentApproval entity)
    {
        return new GetAgentRunApprovalsItem(
            entity.ApprovalId,
            entity.RunId,
            entity.StepId,
            entity.RequestedAction,
            entity.RiskLevel,
            entity.RequestPayload,
            entity.Decision,
            entity.ApproverId,
            entity.ApproverRole,
            entity.ApproverName,
            entity.Comment,
            entity.WaitToken,
            entity.ExpiresAt,
            entity.DecidedAt,
            entity.CreateTime,
            entity.LastModifyTime);
    }
}
