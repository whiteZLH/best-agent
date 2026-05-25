using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class AgentRunRepository : IAgentRunRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentRunRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AgentRun agentRun, CancellationToken cancellationToken)
    {
        await _dbContext.AgentRuns.AddAsync(agentRun, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<AgentRun?> GetByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return _dbContext.AgentRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.RunId == runId && !x.Deleted, cancellationToken);
    }

    public async Task UpdateAsync(AgentRun agentRun, CancellationToken cancellationToken)
    {
        _dbContext.AgentRuns.Update(agentRun);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
