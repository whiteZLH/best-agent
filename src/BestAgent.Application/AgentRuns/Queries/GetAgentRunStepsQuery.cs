using MediatR;

namespace BestAgent.Application.AgentRuns.Queries;

public sealed record GetAgentRunStepsQuery(string RunId) : IRequest<IReadOnlyList<AgentStepModel>>;
