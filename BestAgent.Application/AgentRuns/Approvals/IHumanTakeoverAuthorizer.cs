namespace BestAgent.Application.AgentRuns.Approvals;

public interface IHumanTakeoverAuthorizer
{
    void Authorize(HumanTakeoverAuthorizationContext context);
}

public sealed record HumanTakeoverAuthorizationContext(
    string RunId,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole);
