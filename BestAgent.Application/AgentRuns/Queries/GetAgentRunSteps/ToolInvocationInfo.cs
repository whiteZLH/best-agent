namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record ToolInvocationInfo(
    string InvocationId,
    string ToolName,
    string Mode,
    string Status,
    string CallbackToken,
    DateTime? StartedAt,
    DateTime? EndedAt,
    long DurationMs);
