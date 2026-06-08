namespace BestAgent.Domain.AgentRuns;

public interface IAgentApprovalRepository
{
    Task AddAsync(AgentApproval agentApproval, CancellationToken cancellationToken);

    Task<AgentApproval?> GetByApprovalIdAsync(string approvalId, CancellationToken cancellationToken);

    Task<AgentApproval?> GetByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentApproval>> ListByRunIdAsync(string runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentApproval>> ListExpiredPendingAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);

    Task UpdateAsync(AgentApproval agentApproval, CancellationToken cancellationToken);
}
