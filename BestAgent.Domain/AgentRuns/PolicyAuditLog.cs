using BestAgent.Domain.Common;

namespace BestAgent.Domain.AgentRuns;

public record class PolicyAuditLog : AuditedEntity
{
    public string Id { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string PolicyType { get; init; } = string.Empty;
    public string PolicyName { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string? InputPayload { get; init; }
    public string? ResultPayload { get; init; }
    public string Reason { get; init; } = string.Empty;
}
