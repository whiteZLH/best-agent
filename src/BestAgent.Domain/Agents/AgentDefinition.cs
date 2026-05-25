using System.Text.Json;
using BestAgent.Domain.Common;

namespace BestAgent.Domain.Agents;

public sealed class AgentDefinition : AuditedEntity
{
    public const string DefaultAgentCode = "support-main";
    public const string DefaultAgentId = "agent_def_support_main";

    public string Id { get; set; } = DefaultAgentId;

    public string Code { get; set; } = DefaultAgentCode;

    public string Name { get; set; } = "Support Main Agent";

    public string Description { get; set; } = "Default MVP agent definition.";

    public string Instruction { get; set; } =
        "You are an execution-focused support agent. Answer directly when possible. If helpful, you may call the echo_context tool once before answering.";

    public string DefaultModel { get; set; } = string.Empty;

    public string AllowedToolsJson { get; set; } = """["echo_context"]""";

    public int MaxTurns { get; set; } = 2;

    public bool Enabled { get; set; } = true;

    public IReadOnlyList<string> GetAllowedTools()
    {
        var tools = JsonSerializer.Deserialize<List<string>>(AllowedToolsJson);
        return tools ?? [];
    }
}
