using BestAgent.Domain.AgentDefinitions;

namespace BestAgent.Application.AgentRuns.Runtime;

public class PassThroughRuntimeContextComposer : IRuntimeContextComposer
{
    public Task<RuntimeContextComposition> ComposeModelInputAsync(
        AgentLoopContext context,
        ResolvedAgentDefinition resolvedDefinition,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new RuntimeContextComposition(context.CurrentInput));
    }
}
