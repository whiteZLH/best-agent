using BestAgent.Domain.Tools;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class ToolInvocationRepository : IToolInvocationRepository
{
    private readonly BestAgentDbContext _dbContext;

    public ToolInvocationRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        await _dbContext.ToolInvocations.AddAsync(invocation, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ToolInvocation?> GetByInvocationIdAsync(string invocationId, CancellationToken cancellationToken)
    {
        return _dbContext.ToolInvocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.InvocationId == invocationId && !x.Deleted, cancellationToken);
    }

    public Task<ToolInvocation?> GetPendingByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken)
    {
        return _dbContext.ToolInvocations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.RunId == runId
                    && x.StepId == stepId
                    && x.Status == "Pending"
                    && !x.Deleted,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ToolInvocation>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return await _dbContext.ToolInvocations
            .AsNoTracking()
            .Where(x => x.RunId == runId && !x.Deleted)
            .OrderBy(x => x.CreateTime)
            .ToArrayAsync(cancellationToken);
    }

    public async Task UpdateAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        _dbContext.ToolInvocations.Update(invocation);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
