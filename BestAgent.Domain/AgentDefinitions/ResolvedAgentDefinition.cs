namespace BestAgent.Domain.AgentDefinitions;

public record ResolvedAgentDefinition(
    AgentDefinition Definition,
    AgentDefinitionVersion Version);
