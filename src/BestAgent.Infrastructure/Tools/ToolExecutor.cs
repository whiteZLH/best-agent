using System.Text.Json;
using BestAgent.Application.Abstractions;
using BestAgent.Application.Planning;

namespace BestAgent.Infrastructure.Tools;

internal sealed class ToolExecutor : IToolExecutor
{
    public Task<ToolExecutionResult> ExecuteAsync(string toolName, string argumentsJson, CancellationToken cancellationToken)
    {
        return toolName switch
        {
            "echo_context" => ExecuteEchoContextAsync(argumentsJson),
            _ => Task.FromResult(new ToolExecutionResult("failed", "{}", $"Unsupported tool: {toolName}", """{"durationMs":0}"""))
        };
    }

    private static Task<ToolExecutionResult> ExecuteEchoContextAsync(string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        var text = document.RootElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
        var data = JsonSerializer.Serialize(new { echoedText = text });
        return Task.FromResult(new ToolExecutionResult("succeeded", data, null, """{"durationMs":0,"source":"echo_context"}"""));
    }
}
