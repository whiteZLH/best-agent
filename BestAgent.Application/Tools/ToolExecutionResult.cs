namespace BestAgent.Application.Tools;

public record ToolExecutionResult(
    string ToolName,
    string Output,
    bool IsPending = false,
    string? WaitToken = null,
    string Status = "succeeded",
    string? Error = null,
    string? Meta = null)
{
    public bool IsFailed =>
        string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "error", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(Error);

    public static ToolExecutionResult Completed(string toolName, string output, string? meta = null)
        => new(toolName, output, false, null, "succeeded", null, meta);

    public static ToolExecutionResult Pending(string toolName, string waitToken, string? meta = null)
        => new(toolName, string.Empty, true, waitToken, "pending", null, meta);

    public static ToolExecutionResult Failed(string toolName, string error, string? meta = null)
        => new(toolName, error, false, null, "failed", error, meta);
}
