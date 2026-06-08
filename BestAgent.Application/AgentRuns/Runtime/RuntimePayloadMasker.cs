using BestAgent.Application.Tools;

namespace BestAgent.Application.AgentRuns.Runtime;

public static class RuntimePayloadMasker
{
    public static string? MaskToolInput(string? payload)
    {
        return ToolSensitiveDataMasker.MaskRuntimePayload(payload);
    }

    public static string? MaskToolOutput(string? payload)
    {
        return ToolSensitiveDataMasker.MaskRuntimePayload(payload);
    }
}
