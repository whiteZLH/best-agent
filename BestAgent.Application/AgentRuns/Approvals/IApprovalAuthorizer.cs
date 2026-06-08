namespace BestAgent.Application.AgentRuns.Approvals;

public interface IApprovalAuthorizer
{
    void Authorize(ApprovalAuthorizationContext context);
}

public sealed record ApprovalAuthorizationContext(
    string RunId,
    string StepId,
    string ToolName,
    string SideEffectLevel,
    string? ApproverId,
    string? ApproverName,
    string? ApproverRole,
    string? ApprovalPolicy = null,
    ApprovalPolicyOptions? ApprovalPolicyOptions = null);
