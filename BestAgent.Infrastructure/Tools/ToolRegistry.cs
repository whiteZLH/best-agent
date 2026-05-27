using BestAgent.Application.Tools;

namespace BestAgent.Infrastructure.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, Func<string?, ToolExecutionContext, CancellationToken, Task<ToolExecutionResult>>> _handlers;

    public ToolRegistry()
    {
        _handlers = new Dictionary<string, Func<string?, ToolExecutionContext, CancellationToken, Task<ToolExecutionResult>>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["echo_context"] = ExecuteEchoContextAsync
        };
    }

    public bool TryGet(
        string toolName,
        out Func<string?, ToolExecutionContext, CancellationToken, Task<ToolExecutionResult>>? handler)
    {
        return _handlers.TryGetValue(toolName, out handler);
    }

    private static Task<ToolExecutionResult> ExecuteEchoContextAsync(
        string? input,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var output =
            $$"""
            {
              "runId": "{{context.RunId}}",
              "agentCode": "{{context.AgentCode}}",
              "userInput": "{{EscapeJson(context.UserInput)}}",
              "toolInput": "{{EscapeJson(input ?? string.Empty)}}"
            }
            """;

        return Task.FromResult(new ToolExecutionResult("echo_context", output));
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
