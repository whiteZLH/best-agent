using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;

public class GetAgentRunChildrenQueryHandler : IRequestHandler<GetAgentRunChildrenQuery, IReadOnlyList<GetAgentRunChildrenItem>>
{
    private readonly IAgentRunRepository _agentRunRepository;

    public GetAgentRunChildrenQueryHandler(IAgentRunRepository agentRunRepository)
    {
        _agentRunRepository = agentRunRepository;
    }

    public async Task<IReadOnlyList<GetAgentRunChildrenItem>> Handle(GetAgentRunChildrenQuery request, CancellationToken cancellationToken)
    {
        var childRuns = await _agentRunRepository.ListByParentRunIdAsync(request.RunId, cancellationToken);

        return childRuns
            .Select(agentRun => new GetAgentRunChildrenItem(
                agentRun.RunId,
                agentRun.AgentCode,
                agentRun.Status,
                agentRun.InputPayload,
                agentRun.OutputPayload,
                agentRun.CreateTime,
                agentRun.LastModifyTime,
                agentRun.StartedAt,
                agentRun.EndedAt,
                agentRun.CurrentStepNo,
                string.IsNullOrWhiteSpace(agentRun.ParentRunId) ? null : agentRun.ParentRunId,
                string.IsNullOrWhiteSpace(agentRun.RootRunId) ? null : agentRun.RootRunId,
                string.IsNullOrWhiteSpace(agentRun.DelegatedByRunId) ? null : agentRun.DelegatedByRunId,
                string.IsNullOrWhiteSpace(agentRun.DelegatedByAgent) ? null : agentRun.DelegatedByAgent,
                string.IsNullOrWhiteSpace(agentRun.InterruptReason) ? null : agentRun.InterruptReason,
                string.IsNullOrWhiteSpace(agentRun.CurrentWaitToken) ? null : agentRun.CurrentWaitToken))
            .ToArray();
    }
}
