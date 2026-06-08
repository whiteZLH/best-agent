using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Queries;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunById;

public class GetAgentRunByIdQueryHandler : IRequestHandler<GetAgentRunByIdQuery, GetAgentRunByIdResult?>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentApprovalRepository _agentApprovalRepository;
    private readonly IToolInvocationRepository _toolInvocationRepository;

    public GetAgentRunByIdQueryHandler(
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

    public async Task<GetAgentRunByIdResult?> Handle(GetAgentRunByIdQuery request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
        {
            return null;
        }

        var waitContext = await RunSnapshotWaitContextResolver.ResolveAsync(
            agentRun,
            _agentStepRepository,
            _agentApprovalRepository,
            _toolInvocationRepository,
            cancellationToken);

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
            waitContext.CurrentHandoff);
    }
}
