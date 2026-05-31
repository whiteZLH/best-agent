namespace BestAgent.Api.Contracts.AgentRuns;

public record GetAgentRunApprovalResponse(
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
    DateTime LastModifyTime);
