using BestAgent.Domain.Tools;

namespace BestAgent.Application.Abstractions;

public interface IToolInvocationRepository
{
    Task AddAsync(ToolInvocation invocation, CancellationToken cancellationToken);
}
