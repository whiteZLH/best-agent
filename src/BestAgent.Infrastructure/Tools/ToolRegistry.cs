using BestAgent.Application.Abstractions;
using BestAgent.Domain.Tools;

namespace BestAgent.Infrastructure.Tools;

internal sealed class ToolRegistry : IToolRegistry
{
    private static readonly Dictionary<string, ToolDefinition> Tools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["echo_context"] = new ToolDefinition
        {
            ToolName = "echo_context",
            Description = "Echo back the provided text as structured tool output.",
            Enabled = true
        }
    };

    public ToolDefinition? Get(string toolName)
    {
        return Tools.GetValueOrDefault(toolName);
    }
}
