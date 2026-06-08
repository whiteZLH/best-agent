using BestAgent.Domain.AgentDefinitions;

namespace BestAgent.Application.AgentRuns.Runtime;

public class PassThroughRuntimeContextComposer : IRuntimeContextComposer
{
    public Task<string> ComposeModelInputAsync(
        AgentLoopContext context,
        ResolvedAgentDefinition resolvedDefinition,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(context.CurrentInput);
    }
}
