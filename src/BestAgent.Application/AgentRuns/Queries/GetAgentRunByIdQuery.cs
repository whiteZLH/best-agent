using MediatR;

namespace BestAgent.Application.AgentRuns.Queries;

public sealed record GetAgentRunByIdQuery(string RunId) : IRequest<AgentRunModel>;
