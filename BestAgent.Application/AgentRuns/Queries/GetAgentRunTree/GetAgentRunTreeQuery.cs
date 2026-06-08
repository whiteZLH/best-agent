using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunTree;

public record GetAgentRunTreeQuery(string RunId) : IRequest<GetAgentRunTreeItem?>;
