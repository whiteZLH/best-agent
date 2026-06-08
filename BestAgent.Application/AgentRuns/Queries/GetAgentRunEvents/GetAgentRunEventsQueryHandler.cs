using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;

public class GetAgentRunEventsQueryHandler : IRequestHandler<GetAgentRunEventsQuery, IReadOnlyList<GetAgentRunEventsItem>>
{
    private readonly IRunOutboxEventRepository _runOutboxEventRepository;

    public GetAgentRunEventsQueryHandler(IRunOutboxEventRepository runOutboxEventRepository)
    {
        _runOutboxEventRepository = runOutboxEventRepository;
    }

    public async Task<IReadOnlyList<GetAgentRunEventsItem>> Handle(GetAgentRunEventsQuery request, CancellationToken cancellationToken)
    {
        var events = await _runOutboxEventRepository.ListByRunIdAsync(request.RunId, request.AfterSeqNo, cancellationToken);
        return events
            .Select(GetAgentRunEventsItem.FromEntity)
            .ToArray();
    }
}
