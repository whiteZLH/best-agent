using BestAgent.Application.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestAgent.Application.AgentRuns.Approvals;

public sealed class DefaultApprovalAuthorizer : IApprovalAuthorizer
{
    private readonly ApprovalPolicyOptions _approvalPolicyOptions;
    private readonly ILogger<DefaultApprovalAuthorizer> _logger;

    public DefaultApprovalAuthorizer(
        ApprovalPolicyOptions? approvalPolicyOptions = null,
        ILogger<DefaultApprovalAuthorizer>? logger = null)
    {
        _approvalPolicyOptions = ApprovalPolicyOptionsNormalizer.Normalize(approvalPolicyOptions);
        _logger = logger ?? NullLogger<DefaultApprovalAuthorizer>.Instance;
    }

    public void Authorize(ApprovalAuthorizationContext context)
    {
        var effectivePolicy = ResolveApprovalPolicy(context.ApprovalPolicy, context.ApprovalPolicyOptions);

        if (string.IsNullOrWhiteSpace(context.ApproverId)
            && string.IsNullOrWhiteSpace(context.ApproverName))
        {
            _logger.LogWarning(
                "Approval denied for run {RunId} step {StepId} tool {ToolName}: missing approver identity",
                context.RunId,
                context.StepId,
                context.ToolName);
            throw new ForbiddenException($"Approval for step '{context.StepId}' requires an authenticated or explicit approver identity.");
        }

        if (ApprovalPolicyRules.RequiresApprovalRole(context.SideEffectLevel, effectivePolicy)
            && !ApprovalPolicyRules.HasAllowedApproverRole(context.ApproverRole, effectivePolicy))
        {
            _logger.LogWarning(
                "Approval denied for run {RunId} step {StepId} tool {ToolName}: approver role {ApproverRole} is not allowed for side effect {SideEffectLevel}",
                context.RunId,
                context.StepId,
                context.ToolName,
                context.ApproverRole ?? "none",
                context.SideEffectLevel);
            throw new ForbiddenException($"Approval for tool '{context.ToolName}' requires one of roles: {string.Join(", ", effectivePolicy.AllowedApproverRoles)}.");
        }

        _logger.LogInformation(
            "Approval authorized for run {RunId} step {StepId} tool {ToolName} by approver {ApproverName} with role {ApproverRole}",
            context.RunId,
            context.StepId,
            context.ToolName,
            context.ApproverName ?? context.ApproverId ?? "unknown",
            context.ApproverRole ?? "none");
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
