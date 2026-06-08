using BestAgent.Application.Exceptions;

namespace BestAgent.Application.AgentRuns.Approvals;

public sealed class DefaultApprovalAuthorizer : IApprovalAuthorizer
{
    private readonly ApprovalPolicyOptions _approvalPolicyOptions;

    public DefaultApprovalAuthorizer(ApprovalPolicyOptions? approvalPolicyOptions = null)
    {
        _approvalPolicyOptions = ApprovalPolicyOptionsNormalizer.Normalize(approvalPolicyOptions);
    }

    public void Authorize(ApprovalAuthorizationContext context)
    {
        var effectivePolicy = ResolveApprovalPolicy(context.ApprovalPolicy, context.ApprovalPolicyOptions);

        if (string.IsNullOrWhiteSpace(context.ApproverId)
            && string.IsNullOrWhiteSpace(context.ApproverName))
        {
            throw new ForbiddenException($"Approval for step '{context.StepId}' requires an authenticated or explicit approver identity.");
        }

        if (ApprovalPolicyRules.RequiresApprovalRole(context.SideEffectLevel, effectivePolicy)
            && !ApprovalPolicyRules.HasAllowedApproverRole(context.ApproverRole, effectivePolicy))
        {
            throw new ForbiddenException($"Approval for tool '{context.ToolName}' requires one of roles: {string.Join(", ", effectivePolicy.AllowedApproverRoles)}.");
        }
    }

    private ApprovalPolicyOptions ResolveApprovalPolicy(string? approvalPolicy, ApprovalPolicyOptions? overrideOptions)
    {
        if (overrideOptions is not null)
        {
            return ApprovalPolicyParser.Merge(_approvalPolicyOptions, overrideOptions);
        }

        var versionPolicy = ApprovalPolicyParser.ParseOptional(approvalPolicy);
        return ApprovalPolicyParser.Merge(_approvalPolicyOptions, versionPolicy);
    }
}
