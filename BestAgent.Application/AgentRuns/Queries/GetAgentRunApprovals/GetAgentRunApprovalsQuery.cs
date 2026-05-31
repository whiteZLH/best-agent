using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;

public record GetAgentRunApprovalsQuery(string RunId) : IRequest<IReadOnlyList<GetAgentRunApprovalsItem>>;
