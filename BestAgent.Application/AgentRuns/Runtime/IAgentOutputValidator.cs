namespace BestAgent.Application.AgentRuns.Runtime;

public interface IAgentOutputValidator
{
    void Validate(string agentCode, string? outputSchema, string output);
}
