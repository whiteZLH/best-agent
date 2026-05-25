namespace BestAgent.Application.Planning;

public sealed record ToolExecutionResult(
    string Status,
    string DataJson,
    string? Error,
    string MetaJson);
