using BestAgent.Application.Exceptions;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CompleteHumanAgentRun;

public class CompleteHumanAgentRunCommandHandler : IRequestHandler<CompleteHumanAgentRunCommand, CompleteHumanAgentRunResult>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentRunChannel _agentRunChannel;
    private readonly IHumanTakeoverAuthorizer _humanTakeoverAuthorizer;

    public CompleteHumanAgentRunCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentRunChannel agentRunChannel,
        IHumanTakeoverAuthorizer humanTakeoverAuthorizer)
    {
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentRunChannel = agentRunChannel;
        _humanTakeoverAuthorizer = humanTakeoverAuthorizer;
    }

    public async Task<CompleteHumanAgentRunResult> Handle(CompleteHumanAgentRunCommand request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
        {
            throw new NotFoundException("AgentRun", request.RunId);
        }

        if (!string.Equals(agentRun.Status, "WaitingHuman", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Run '{request.RunId}' is in status '{agentRun.Status}', expected 'WaitingHuman'.");
        }

        if (!string.Equals(agentRun.CurrentWaitToken, request.WaitToken, StringComparison.Ordinal))
        {
            throw new ConflictException($"Wait token mismatch for run '{request.RunId}'.");
        }

        var pendingStep = await _agentStepRepository.GetLastByRunIdAsync(request.RunId, cancellationToken);
        if (pendingStep is null
            || !string.Equals(pendingStep.StepId, request.StepId, StringComparison.Ordinal)
            || !string.Equals(pendingStep.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Step '{request.StepId}' is not the current pending human step for run '{request.RunId}'.");
        }

        if (!HumanApprovalPayloadSerializer.TryParse(pendingStep.DecisionPayload, out var humanPayload)
            || !string.Equals(humanPayload!.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Step '{request.StepId}' is not waiting for human takeover.");
        }

        _humanTakeoverAuthorizer.Authorize(new HumanTakeoverAuthorizationContext(
            request.RunId,
            request.HumanOperatorId,
            request.HumanOperatorName,
            request.HumanOperatorRole));

        agentRun = agentRun with
        {
            Status = "Running",
            CurrentWaitToken = string.Empty,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = DateTime.UtcNow
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

        await _agentRunChannel.EnqueueAsync(
            new CompleteHumanAgentRunMessage(
                request.RunId,
                request.StepId,
                request.WaitToken,
                request.HumanResult,
                request.Comment,
                request.Terminate,
                request.HumanOperatorId,
                request.HumanOperatorName,
                request.HumanOperatorRole),
            cancellationToken);

        return new CompleteHumanAgentRunResult(
            agentRun.RunId,
            agentRun.AgentCode,
            agentRun.InputPayload,
            agentRun.OutputPayload,
            agentRun.Status);
    }
}
