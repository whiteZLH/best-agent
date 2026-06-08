using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using BestAgent.Application.AgentRuns.Queries;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;

public class GetAgentRunChildrenQueryHandler : IRequestHandler<GetAgentRunChildrenQuery, IReadOnlyList<GetAgentRunChildrenItem>>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentApprovalRepository _agentApprovalRepository;
    private readonly IToolInvocationRepository _toolInvocationRepository;

    public GetAgentRunChildrenQueryHandler(
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

    public async Task<IReadOnlyList<GetAgentRunChildrenItem>> Handle(GetAgentRunChildrenQuery request, CancellationToken cancellationToken)
    {
        var childRuns = await _agentRunRepository.ListByParentRunIdAsync(request.RunId, cancellationToken);

        var results = new List<GetAgentRunChildrenItem>(childRuns.Count);
        foreach (var agentRun in childRuns)
        {
            var waitContext = await RunSnapshotWaitContextResolver.ResolveAsync(
                agentRun,
                _agentStepRepository,
                _agentApprovalRepository,
                _toolInvocationRepository,
                cancellationToken);
            results.Add(new GetAgentRunChildrenItem(
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
                waitContext.CurrentHandoff));
        }

        return results;
    }
}
