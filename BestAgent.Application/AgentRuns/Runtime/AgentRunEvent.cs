namespace BestAgent.Application.AgentRuns.Runtime;

public record AgentRunEvent(string RunId, string EventType, AgentRunEventData Data);

public record AgentRunEventData(
    int StepNo,
    string StepType,
    string Status,
    string? Output = null,
    string? Error = null);
