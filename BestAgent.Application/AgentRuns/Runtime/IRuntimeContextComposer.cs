using BestAgent.Domain.AgentDefinitions;

namespace BestAgent.Application.AgentRuns.Runtime;

public interface IRuntimeContextComposer
{
    Task<RuntimeContextComposition> ComposeModelInputAsync(
        AgentLoopContext context,
        ResolvedAgentDefinition resolvedDefinition,
        CancellationToken cancellationToken);
}
