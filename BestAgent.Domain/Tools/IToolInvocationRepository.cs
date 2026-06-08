namespace BestAgent.Domain.Tools;

public interface IToolInvocationRepository
{
    Task AddAsync(ToolInvocation invocation, CancellationToken cancellationToken);

    Task<ToolInvocation?> GetByInvocationIdAsync(string invocationId, CancellationToken cancellationToken);

    Task<ToolInvocation?> GetPendingByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ToolInvocation>> ListByRunIdAsync(string runId, CancellationToken cancellationToken);

    Task UpdateAsync(ToolInvocation invocation, CancellationToken cancellationToken);
}
