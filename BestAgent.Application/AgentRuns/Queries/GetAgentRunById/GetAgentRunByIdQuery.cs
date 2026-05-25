using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunById;

public record GetAgentRunByIdQuery(string RunId) : IRequest<GetAgentRunByIdResult?>;
