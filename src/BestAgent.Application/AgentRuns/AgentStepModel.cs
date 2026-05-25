namespace BestAgent.Application.AgentRuns;

public sealed record AgentStepModel(
    string StepId,
    int StepNo,
    string StepType,
    string Status,
    string InputPayload,
    string? OutputPayload,
    string? ErrorPayload);
