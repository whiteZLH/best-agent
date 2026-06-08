using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Runtime;

public interface IRuntimeMemoryWriter
{
    Task RecordToolResultAsync(
        AgentRun run,
        string toolName,
        string? toolInput,
        string? toolOutput,
        bool persistSessionMemory,
        bool persistUserMemory,
        CancellationToken cancellationToken);

    Task RecordRunCompletionSummaryAsync(
        AgentRun run,
        string? finalOutput,
        CancellationToken cancellationToken);
}
