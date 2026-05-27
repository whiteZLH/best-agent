namespace BestAgent.Application.Tools;

public record ToolExecutionResult(
    string ToolName,
    string Output,
    bool IsPending = false,
    string? WaitToken = null)
{
    public static ToolExecutionResult Completed(string toolName, string output)
        => new(toolName, output, false, null);

    public static ToolExecutionResult Pending(string toolName, string waitToken)
        => new(toolName, string.Empty, true, waitToken);
}
