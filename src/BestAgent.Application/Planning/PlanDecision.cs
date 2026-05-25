using System.Text.Json;

namespace BestAgent.Application.Planning;

public sealed record PlanDecision(
    PlanDecisionType Type,
    string Reason,
    string? ResponseMessage,
    IReadOnlyList<ToolCallPlan> ToolCalls,
    string? SelectedModel)
{
    public static PlanDecision Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var typeValue = root.GetProperty("type").GetString();

        var type = typeValue?.ToLowerInvariant() switch
        {
            "respond" => PlanDecisionType.Respond,
            "tool_call" => PlanDecisionType.ToolCall,
            _ => throw new InvalidOperationException("Unsupported plan type.")
        };

        var reason = root.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString() ?? string.Empty
            : string.Empty;
        var responseMessage = root.TryGetProperty("responseMessage", out var messageElement)
            ? messageElement.GetString()
            : null;
        var selectedModel = root.TryGetProperty("selectedModel", out var modelElement)
            ? modelElement.GetString()
            : null;

        var toolCalls = new List<ToolCallPlan>();
        if (root.TryGetProperty("toolCalls", out var toolCallElement) &&
            toolCallElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in toolCallElement.EnumerateArray())
            {
                var toolName = item.GetProperty("toolName").GetString() ?? string.Empty;
                var argumentsJson = item.TryGetProperty("arguments", out var arguments)
                    ? arguments.GetRawText()
                    : "{}";
                toolCalls.Add(new ToolCallPlan(toolName, argumentsJson));
            }
        }

        return new PlanDecision(type, reason, responseMessage, toolCalls, selectedModel);
    }
}
