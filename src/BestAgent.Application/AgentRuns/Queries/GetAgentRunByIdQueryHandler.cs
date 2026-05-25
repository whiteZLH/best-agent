using BestAgent.Application.Abstractions;
using BestAgent.Application.Common;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries;

public sealed class GetAgentRunByIdQueryHandler : IRequestHandler<GetAgentRunByIdQuery, AgentRunModel>
{
    private readonly IAgentRunRepository _agentRunRepository;

    public GetAgentRunByIdQueryHandler(IAgentRunRepository agentRunRepository)
    {
        _agentRunRepository = agentRunRepository;
    }

    public async Task<AgentRunModel> Handle(GetAgentRunByIdQuery request, CancellationToken cancellationToken)
    {
        var run = await _agentRunRepository.GetByIdAsync(request.RunId, cancellationToken)
            ?? throw new EntityNotFoundException($"Run {request.RunId} was not found.");

        return new AgentRunModel(
            run.RunId,
            run.AgentCode,
            run.Status.ToString(),
            run.OutputPayload,
            run.ErrorMessage,
            run.CurrentStepNo,
            run.IdempotencyKey);
    }
}
