using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;

public record GetAgentRunEventsQuery(string RunId, long? AfterSeqNo = null) : IRequest<IReadOnlyList<GetAgentRunEventsItem>>;
