using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Runtime;

public sealed class NoOpRuntimeMemoryWriter : IRuntimeMemoryWriter
{
    public Task RecordToolResultAsync(
        AgentRun run,
        string toolName,
        string? toolInput,
        string? toolOutput,
        bool persistSessionMemory,
        bool persistUserMemory,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task RecordRunCompletionSummaryAsync(
        AgentRun run,
        string? finalOutput,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
