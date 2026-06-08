using BestAgent.Application.Exceptions;

namespace BestAgent.Application.AgentRuns.Approvals;

public sealed class DefaultHumanTakeoverAuthorizer : IHumanTakeoverAuthorizer
{
    public void Authorize(HumanTakeoverAuthorizationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HumanOperatorId)
            && string.IsNullOrWhiteSpace(context.HumanOperatorName))
        {
            throw new ForbiddenException(
                $"Human takeover for run '{context.RunId}' requires an authenticated or explicit operator identity.");
        }
    }
}
