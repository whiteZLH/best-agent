using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public class GetAgentRunStepsQueryHandler : IRequestHandler<GetAgentRunStepsQuery, IReadOnlyList<GetAgentRunStepsItem>>
{
    private readonly IAgentStepRepository _agentStepRepository;

    public GetAgentRunStepsQueryHandler(IAgentStepRepository agentStepRepository)
    {
        _agentStepRepository = agentStepRepository;
    }

    public async Task<IReadOnlyList<GetAgentRunStepsItem>> Handle(GetAgentRunStepsQuery request, CancellationToken cancellationToken)
    {
        var steps = await _agentStepRepository.ListByRunIdAsync(request.RunId, cancellationToken);

        return steps
            .Select(step => new GetAgentRunStepsItem(
                step.StepId,
                step.StepNo,
                step.StepType,
                step.Status,
                step.InputPayload,
                step.OutputPayload,
                step.ErrorPayload,
                step.StepKey,
                step.CreateTime,
                step.LastModifyTime,
                step.StartedAt,
                step.EndedAt,
                step.DurationMs))
            .ToList();
    }
}
