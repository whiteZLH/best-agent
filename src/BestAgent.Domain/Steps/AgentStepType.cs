namespace BestAgent.Domain.Steps;

public enum AgentStepType
{
    Input = 0,
    Plan = 1,
    ToolCall = 2,
    ToolResult = 3,
    Respond = 4,
    Interrupt = 5
}
