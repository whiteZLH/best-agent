using BestAgent.Domain.AgentDefinitions;

namespace BestAgent.Application.AgentRuns.Runtime;

public interface IRuntimeContextComposer
{
    Task<string> ComposeModelInputAsync(
        AgentLoopContext context,
        ResolvedAgentDefinition resolvedDefinition,
        CancellationToken cancellationToken);
}
