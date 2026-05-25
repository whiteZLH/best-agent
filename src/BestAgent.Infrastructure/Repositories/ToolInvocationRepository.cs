using BestAgent.Application.Abstractions;
using BestAgent.Domain.Tools;
using BestAgent.Infrastructure.Persistence;

namespace BestAgent.Infrastructure.Repositories;

internal sealed class ToolInvocationRepository : IToolInvocationRepository
{
    private readonly BestAgentDbContext _dbContext;

    public ToolInvocationRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        return _dbContext.ToolInvocations.AddAsync(invocation, cancellationToken).AsTask();
    }
}
