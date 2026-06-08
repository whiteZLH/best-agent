using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;

public record GetAgentRunChildrenQuery(string RunId) : IRequest<IReadOnlyList<GetAgentRunChildrenItem>>;
