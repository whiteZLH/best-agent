using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;

public class RejectAgentRunStepCommandHandler : IRequestHandler<RejectAgentRunStepCommand, RejectAgentRunStepResult>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentRunChannel _agentRunChannel;

    public RejectAgentRunStepCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentRunChannel agentRunChannel)
    {
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentRunChannel = agentRunChannel;
    }

    public async Task<RejectAgentRunStepResult> Handle(RejectAgentRunStepCommand request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
            throw new NotFoundException("AgentRun", request.RunId);

        if (agentRun.Status != "WaitingApproval")
            throw new ConflictException($"Run '{request.RunId}' is in status '{agentRun.Status}', expected 'WaitingApproval'.");

        var pendingStep = await _agentStepRepository.GetLastByRunIdAsync(request.RunId, cancellationToken);
        if (pendingStep is null || pendingStep.StepId != request.StepId || pendingStep.Status != "Pending")
            throw new ConflictException($"Step '{request.StepId}' is not the current pending approval step for run '{request.RunId}'.");

        if (!ApprovalPayloadSerializer.TryParse(pendingStep.DecisionPayload, out var payload)
            || !string.Equals(payload!.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException($"Step '{request.StepId}' is not waiting for approval.");

        agentRun = agentRun with
        {
            Status = "Running",
            CurrentWaitToken = string.Empty,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = DateTime.UtcNow
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

        await _agentRunChannel.EnqueueAsync(
            new RejectAgentRunStepMessage(
                request.RunId,
                request.StepId,
                request.Comment,
                request.ApproverId,
                request.ApproverName,
                request.ApproverRole),
            cancellationToken);

        return new RejectAgentRunStepResult(agentRun.RunId, agentRun.AgentCode, agentRun.InputPayload, null, "Running");
    }
}
