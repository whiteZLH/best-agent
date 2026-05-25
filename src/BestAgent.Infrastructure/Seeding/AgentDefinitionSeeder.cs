using BestAgent.Domain.Agents;

namespace BestAgent.Infrastructure.Seeding;

internal static class AgentDefinitionSeeder
{
    public static AgentDefinition CreateDefault(string modelName, DateTimeOffset now)
    {
        return new AgentDefinition
        {
            Id = AgentDefinition.DefaultAgentId,
            Code = AgentDefinition.DefaultAgentCode,
            Name = "Support Main Agent",
            Description = "Default seed agent for the MVP runtime.",
            Instruction = "You are an execution-focused support agent. Reply directly when enough context exists. If the user requests echoing or reflection, call echo_context once and then answer.",
            DefaultModel = modelName,
            AllowedToolsJson = """["echo_context"]""",
            MaxTurns = 2,
            Enabled = true,
            LastModifier = "system",
            LastModifierName = "system",
            LastModifyTime = now,
            CreateTime = now,
            CreatorName = "system",
            Creator = "system",
            Deleted = false
        };
    }
}
