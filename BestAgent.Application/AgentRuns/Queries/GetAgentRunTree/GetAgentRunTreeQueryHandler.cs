using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunTree;

public class GetAgentRunTreeQueryHandler : IRequestHandler<GetAgentRunTreeQuery, GetAgentRunTreeItem?>
{
    private readonly IAgentRunRepository _agentRunRepository;

    public GetAgentRunTreeQueryHandler(IAgentRunRepository agentRunRepository)
    {
        _agentRunRepository = agentRunRepository;
    }

    public async Task<GetAgentRunTreeItem?> Handle(GetAgentRunTreeQuery request, CancellationToken cancellationToken)
    {
        var rootRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (rootRun is null)
        {
            return null;
        }

        return await BuildNodeAsync(rootRun, cancellationToken);
    }

    private async Task<GetAgentRunTreeItem> BuildNodeAsync(AgentRun agentRun, CancellationToken cancellationToken)
    {
        var childRuns = await _agentRunRepository.ListByParentRunIdAsync(agentRun.RunId, cancellationToken);
        var children = new List<GetAgentRunTreeItem>(childRuns.Count);
        foreach (var childRun in childRuns)
        {
            children.Add(await BuildNodeAsync(childRun, cancellationToken));
        }

        return new GetAgentRunTreeItem(
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
            string.IsNullOrWhiteSpace(agentRun.CurrentWaitToken) ? null : agentRun.CurrentWaitToken,
            children);
    }
}
