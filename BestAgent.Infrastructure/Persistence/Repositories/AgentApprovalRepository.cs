using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class AgentApprovalRepository : IAgentApprovalRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentApprovalRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AgentApproval agentApproval, CancellationToken cancellationToken)
    {
        await _dbContext.AgentApprovals.AddAsync(agentApproval, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AgentApproval?> GetByApprovalIdAsync(string approvalId, CancellationToken cancellationToken)
    {
        return await _dbContext.AgentApprovals
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApprovalId == approvalId && !x.Deleted, cancellationToken);
    }

    public async Task<AgentApproval?> GetByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken)
    {
        return await _dbContext.AgentApprovals
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == runId && x.StepId == stepId && !x.Deleted, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentApproval>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return await _dbContext.AgentApprovals
            .AsNoTracking()
            .Where(x => x.RunId == runId && !x.Deleted)
            .OrderBy(x => x.CreateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentApproval>> ListExpiredPendingAsync(
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AgentApprovals
            .AsNoTracking()
            .Where(x =>
                !x.Deleted &&
                x.ExpiresAt != null &&
                x.ExpiresAt <= utcNow &&
                x.Decision == "Pending")
            .OrderBy(x => x.ExpiresAt)
            .ThenBy(x => x.CreateTime)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(AgentApproval agentApproval, CancellationToken cancellationToken)
    {
        _dbContext.AgentApprovals.Update(agentApproval);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
