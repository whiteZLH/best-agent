using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public sealed record MemoryWritePolicy(
    bool ToolResultMemoryEnabled,
    IReadOnlySet<string> AllowedToolNames,
    bool UserMemoryWriteEnabled,
    bool SummaryMemoryWriteEnabled)
{
    public static MemoryWritePolicy Parse(string? json)
    {
        var policy = MemoryPolicy.Parse(json);
        return new MemoryWritePolicy(
            policy.ToolResultMemoryEnabled,
            policy.AllowedToolNames,
            policy.UserMemoryWriteEnabled,
            policy.SummaryMemoryWriteEnabled);
    }

    public bool AllowsToolResultWrite(string? toolName)
    {
        if (!ToolResultMemoryEnabled)
        {
            return false;
        }

        if (AllowedToolNames.Count == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(toolName)
            && AllowedToolNames.Contains(toolName.Trim());
    }

    public bool AllowsUserMemoryWrite()
    {
        return UserMemoryWriteEnabled;
    }

    public bool AllowsSummaryMemoryWrite()
    {
        return SummaryMemoryWriteEnabled;
    }
}
