using BestAgent.Application.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestAgent.Application.AgentRuns.Approvals;

public sealed class DefaultHumanTakeoverAuthorizer : IHumanTakeoverAuthorizer
{
    private readonly HumanTakeoverPolicyOptions _humanTakeoverPolicyOptions;
    private readonly ILogger<DefaultHumanTakeoverAuthorizer> _logger;

    public DefaultHumanTakeoverAuthorizer(
        HumanTakeoverPolicyOptions? humanTakeoverPolicyOptions = null,
        ILogger<DefaultHumanTakeoverAuthorizer>? logger = null)
    {
        _humanTakeoverPolicyOptions = HumanTakeoverPolicyOptionsNormalizer.Normalize(humanTakeoverPolicyOptions);
        _logger = logger ?? NullLogger<DefaultHumanTakeoverAuthorizer>.Instance;
    }

    public void Authorize(HumanTakeoverAuthorizationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HumanOperatorId)
            && string.IsNullOrWhiteSpace(context.HumanOperatorName))
        {
            _logger.LogWarning(
                "Human takeover denied for run {RunId}: missing operator identity",
                context.RunId);
            throw new ForbiddenException(
                $"Human takeover for run '{context.RunId}' requires an authenticated or explicit operator identity.");
        }

        if (_humanTakeoverPolicyOptions.AllowedHumanOperatorRoles.Count > 0
            && !HasAllowedHumanOperatorRole(context.HumanOperatorRole))
        {
            _logger.LogWarning(
                "Human takeover denied for run {RunId}: operator role {HumanOperatorRole} is not allowed",
                context.RunId,
                context.HumanOperatorRole ?? "none");
            throw new ForbiddenException(
                $"Human takeover for run '{context.RunId}' requires one of roles: {string.Join(", ", _humanTakeoverPolicyOptions.AllowedHumanOperatorRoles)}.");
        }

        _logger.LogInformation(
            "Human takeover authorized for run {RunId} by operator {HumanOperatorName} with role {HumanOperatorRole}",
            context.RunId,
            context.HumanOperatorName ?? context.HumanOperatorId ?? "unknown",
            context.HumanOperatorRole ?? "none");
    }

    private bool HasAllowedHumanOperatorRole(string? humanOperatorRole)
    {
        if (string.IsNullOrWhiteSpace(humanOperatorRole))
        {
            return false;
        }

        return humanOperatorRole
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(role => _humanTakeoverPolicyOptions.AllowedHumanOperatorRoles.Any(
                allowedRole => string.Equals(allowedRole, role, StringComparison.OrdinalIgnoreCase)));
    }
}
