using BestAgent.Application.Abstractions;
using BestAgent.Application.Planning;

namespace BestAgent.IntegrationTests.Api;

internal sealed class SequenceModelGateway : IModelGateway
{
    private readonly Queue<Func<PlanDecision>> _responses;

    public SequenceModelGateway(IEnumerable<Func<PlanDecision>> responses)
    {
        _responses = new Queue<Func<PlanDecision>>(responses);
    }

    public Task<PlanDecision> PlanAsync(ModelContext context, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No planned responses remain.");
        }

        return Task.FromResult(_responses.Dequeue().Invoke());
    }
}
