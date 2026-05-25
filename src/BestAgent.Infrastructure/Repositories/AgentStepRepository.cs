using BestAgent.Application.Abstractions;
using BestAgent.Domain.Steps;
using BestAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Repositories;

internal sealed class AgentStepRepository : IAgentStepRepository
{
    private readonly BestAgentDbContext _dbContext;

    public AgentStepRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(AgentStep step, CancellationToken cancellationToken)
    {
        return _dbContext.AgentSteps.AddAsync(step, cancellationToken).AsTask();
    }

    public async Task<IReadOnlyList<AgentStep>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        return await _dbContext.AgentSteps
            .Where(entity => entity.RunId == runId)
            .OrderBy(entity => entity.StepNo)
            .ToListAsync(cancellationToken);
    }
}
