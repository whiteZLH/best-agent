using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using BestAgent.Application.AgentRuns.Queries;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunTree;

public class GetAgentRunTreeQueryHandler : IRequestHandler<GetAgentRunTreeQuery, GetAgentRunTreeItem?>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentApprovalRepository _agentApprovalRepository;
    private readonly IToolInvocationRepository _toolInvocationRepository;

    public GetAgentRunTreeQueryHandler(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentApprovalRepository agentApprovalRepository,
        IToolInvocationRepository toolInvocationRepository)
    {
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentApprovalRepository = agentApprovalRepository;
        _toolInvocationRepository = toolInvocationRepository;
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
        var waitContext = await RunSnapshotWaitContextResolver.ResolveAsync(
            agentRun,
            _agentStepRepository,
            _agentApprovalRepository,
            _toolInvocationRepository,
            cancellationToken);

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
            waitContext.CurrentStepId,
            waitContext.WaitStepType,
            waitContext.CurrentInvocationId,
            waitContext.CurrentApprovalId,
            waitContext.CurrentToolInvocation,
            waitContext.CurrentApproval,
            waitContext.CurrentHumanWait,
            waitContext.CurrentHandoff,
            children);
    }
}
