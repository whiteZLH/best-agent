namespace BestAgent.Domain.Tools;

public sealed class ToolDefinition
{
    public string ToolName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;
}
