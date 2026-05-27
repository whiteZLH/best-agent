namespace BestAgent.Application.Tools;

public record ToolExecutionContext(
    string RunId,
    string AgentCode,
    string UserInput);
