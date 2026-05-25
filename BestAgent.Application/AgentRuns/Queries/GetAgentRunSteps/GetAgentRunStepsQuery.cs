using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public record GetAgentRunStepsQuery(string RunId) : IRequest<IReadOnlyList<GetAgentRunStepsItem>>;
