namespace BestAgent.Api.Contracts.AgentRuns;

public record ToolInvocationInfoResponse(
    string InvocationId,
    string ToolName,
    string Mode,
    string Status,
    string CallbackToken,
    DateTime? StartedAt,
    DateTime? EndedAt,
    long DurationMs);
