using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class AgentStepRepository : IAgentStepRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentStepRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AgentStep agentStep, CancellationToken cancellationToken)
    {
        await _dbContext.AgentSteps.AddAsync(agentStep, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentStep>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return await _dbContext.AgentSteps
            .AsNoTracking()
            .Where(x => x.RunId == runId && !x.Deleted)
            .OrderBy(x => x.StepNo)
            .ToListAsync(cancellationToken);
    }
}
