using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunById;

public class GetAgentRunByIdQueryHandler : IRequestHandler<GetAgentRunByIdQuery, GetAgentRunByIdResult?>
{
    private readonly IAgentRunRepository _agentRunRepository;

    public GetAgentRunByIdQueryHandler(IAgentRunRepository agentRunRepository)
    {
        _agentRunRepository = agentRunRepository;
    }

    public async Task<GetAgentRunByIdResult?> Handle(GetAgentRunByIdQuery request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
        {
            return null;
        }

        return new GetAgentRunByIdResult(
            agentRun.RunId,
            agentRun.AgentCode,
            agentRun.Status,
            agentRun.InputPayload,
            agentRun.OutputPayload,
            agentRun.MaxTurns,
            agentRun.MaxCost,
            agentRun.TotalCost,
            agentRun.CreateTime,
            agentRun.LastModifyTime,
            agentRun.StartedAt,
            agentRun.EndedAt);
    }
}
