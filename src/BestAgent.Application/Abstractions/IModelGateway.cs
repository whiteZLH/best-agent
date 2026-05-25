using BestAgent.Application.Planning;

namespace BestAgent.Application.Abstractions;

public interface IModelGateway
{
    Task<PlanDecision> PlanAsync(ModelContext context, CancellationToken cancellationToken);
}
