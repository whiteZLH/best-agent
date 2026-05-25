using BestAgent.Application.Abstractions;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries;

public sealed class GetAgentRunStepsQueryHandler : IRequestHandler<GetAgentRunStepsQuery, IReadOnlyList<AgentStepModel>>
{
    private readonly IAgentStepRepository _agentStepRepository;

    public GetAgentRunStepsQueryHandler(IAgentStepRepository agentStepRepository)
    {
        _agentStepRepository = agentStepRepository;
    }

    public async Task<IReadOnlyList<AgentStepModel>> Handle(GetAgentRunStepsQuery request, CancellationToken cancellationToken)
    {
        var steps = await _agentStepRepository.ListByRunIdAsync(request.RunId, cancellationToken);
        return steps
            .OrderBy(step => step.StepNo)
            .Select(step => new AgentStepModel(
                step.StepId,
                step.StepNo,
                step.StepType.ToString(),
                step.Status.ToString(),
                step.InputPayload,
                step.OutputPayload,
                step.ErrorPayload))
            .ToList();
    }
}
