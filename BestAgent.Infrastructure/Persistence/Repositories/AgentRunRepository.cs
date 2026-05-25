using BestAgent.Domain.AgentRuns;

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
}
