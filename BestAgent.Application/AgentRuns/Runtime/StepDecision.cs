namespace BestAgent.Application.AgentRuns.Runtime;

public record StepDecision(
    string Action,
    string? Response,
    string? ToolName,
    string? ToolInput)
{
    public static StepDecision Respond(string response)
    {
        return new("respond", response, null, null);
    }

    public static StepDecision ToolCall(string toolName, string? toolInput)
    {
        return new("tool_call", null, toolName, toolInput);
    }
}
