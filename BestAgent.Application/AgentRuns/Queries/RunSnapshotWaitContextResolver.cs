using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;

namespace BestAgent.Application.AgentRuns.Queries;

internal static class RunSnapshotWaitContextResolver
{
    public static async Task<RunSnapshotWaitContext> ResolveAsync(
        AgentRun agentRun,
        IAgentStepRepository agentStepRepository,
        IAgentApprovalRepository agentApprovalRepository,
        IToolInvocationRepository toolInvocationRepository,
        CancellationToken cancellationToken)
    {
        if (agentRun.CurrentStepNo <= 0)
        {
            return RunSnapshotWaitContext.Empty;
        }

        var currentStep = await agentStepRepository.GetLastByRunIdAsync(agentRun.RunId, cancellationToken);
        if (currentStep is null || currentStep.StepNo != agentRun.CurrentStepNo)
        {
            return RunSnapshotWaitContext.Empty;
        }

        var currentStepId = currentStep.StepId;
        if (string.IsNullOrWhiteSpace(agentRun.CurrentWaitToken)
            && !agentRun.Status.StartsWith("Waiting", StringComparison.OrdinalIgnoreCase))
        {
            return new RunSnapshotWaitContext(CurrentStepId: currentStepId);
        }

        var pendingInvocation = await toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(
            agentRun.RunId,
            currentStep.StepId,
            cancellationToken);
        var approval = await agentApprovalRepository.GetByRunIdAndStepIdAsync(
            agentRun.RunId,
            currentStep.StepId,
            cancellationToken);

        return new RunSnapshotWaitContext(
            currentStepId,
            currentStep.StepType,
            pendingInvocation?.InvocationId,
            approval?.ApprovalId,
            AgentRunStepDataMapper.MapToolInvocation(pendingInvocation),
            AgentRunStepDataMapper.MapApproval(approval, currentStep.DecisionPayload),
            AgentRunStepDataMapper.MapHumanWait(currentStep.DecisionPayload),
            AgentRunStepDataMapper.MapHandoff(currentStep.DecisionPayload));
    }
}

internal sealed record RunSnapshotWaitContext(
    string? CurrentStepId = null,
    string? WaitStepType = null,
    string? CurrentInvocationId = null,
    string? CurrentApprovalId = null,
    ToolInvocationInfo? CurrentToolInvocation = null,
    ApprovalInfo? CurrentApproval = null,
    HumanWaitInfo? CurrentHumanWait = null,
    HandoffInfo? CurrentHandoff = null)
{
    public static RunSnapshotWaitContext Empty { get; } = new();
}
