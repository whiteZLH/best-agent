using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

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

        var currentStepId = default(string);
        var waitStepType = default(string);
        var currentInvocationId = default(string);
        var currentApprovalId = default(string);
        ToolInvocationInfo? currentToolInvocation = null;
        ApprovalInfo? currentApproval = null;
        HumanWaitInfo? currentHumanWait = null;
        HandoffInfo? currentHandoff = null;
        var currentStep = agentRun.CurrentStepNo > 0
            ? await _agentStepRepository.GetLastByRunIdAsync(request.RunId, cancellationToken)
            : null;
        if (currentStep is not null
            && currentStep.StepNo == agentRun.CurrentStepNo)
        {
            currentStepId = currentStep.StepId;
            if (!string.IsNullOrWhiteSpace(agentRun.CurrentWaitToken)
                || agentRun.Status.StartsWith("Waiting", StringComparison.OrdinalIgnoreCase))
            {
                waitStepType = currentStep.StepType;

                var pendingInvocation = await _toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(
                    request.RunId,
                    currentStep.StepId,
                    cancellationToken);
                currentInvocationId = pendingInvocation?.InvocationId;
                currentToolInvocation = AgentRunStepDataMapper.MapToolInvocation(pendingInvocation);

                var approval = await _agentApprovalRepository.GetByRunIdAndStepIdAsync(
                    request.RunId,
                    currentStep.StepId,
                    cancellationToken);
                currentApprovalId = approval?.ApprovalId;
                currentApproval = AgentRunStepDataMapper.MapApproval(approval, currentStep.DecisionPayload);
                currentHumanWait = AgentRunStepDataMapper.MapHumanWait(currentStep.DecisionPayload);
                currentHandoff = AgentRunStepDataMapper.MapHandoff(currentStep.DecisionPayload);
            }
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
            agentRun.EndedAt,
            agentRun.CurrentStepNo,
            string.IsNullOrWhiteSpace(agentRun.ParentRunId) ? null : agentRun.ParentRunId,
            string.IsNullOrWhiteSpace(agentRun.RootRunId) ? null : agentRun.RootRunId,
            string.IsNullOrWhiteSpace(agentRun.DelegatedByRunId) ? null : agentRun.DelegatedByRunId,
            string.IsNullOrWhiteSpace(agentRun.DelegatedByAgent) ? null : agentRun.DelegatedByAgent,
            string.IsNullOrWhiteSpace(agentRun.InterruptReason) ? null : agentRun.InterruptReason,
            string.IsNullOrWhiteSpace(agentRun.CurrentWaitToken) ? null : agentRun.CurrentWaitToken,
            currentStepId,
            waitStepType,
            currentInvocationId,
            currentApprovalId,
            currentToolInvocation,
            currentApproval,
            currentHumanWait,
            currentHandoff);
    }
}
