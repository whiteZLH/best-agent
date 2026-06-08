using BestAgent.Application.Exceptions;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentRuns;
using MediatR;
using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Commands.CancelAgentRun;

public class CancelAgentRunCommandHandler : IRequestHandler<CancelAgentRunCommand, CancelAgentRunResult>
{
    private static readonly HashSet<string> CancellableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Running",
        "WaitingTool",
        "WaitingApproval",
        "WaitingHandoff"
    };

    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IRunOutboxEventRepository _runOutboxEventRepository;
    private readonly IAgentRunEventBus _agentRunEventBus;
    private readonly IAgentMetrics _agentMetrics;

    public CancelAgentRunCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IRunOutboxEventRepository runOutboxEventRepository,
        IAgentRunEventBus agentRunEventBus,
        IAgentMetrics agentMetrics)
    {
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _runOutboxEventRepository = runOutboxEventRepository;
        _agentRunEventBus = agentRunEventBus;
        _agentMetrics = agentMetrics;
    }

    public async Task<CancelAgentRunResult> Handle(CancelAgentRunCommand request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
        {
            throw new NotFoundException("AgentRun", request.RunId);
        }

        if (!CancellableStatuses.Contains(agentRun.Status))
        {
            return ToResult(agentRun);
        }

        var now = DateTime.UtcNow;
        var reason = NormalizeReason(request.Reason);

        var pendingStep = await _agentStepRepository.GetLastByRunIdAsync(agentRun.RunId, cancellationToken);
        if (pendingStep is not null && string.Equals(pendingStep.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            pendingStep = pendingStep with
            {
                Status = "Cancelled",
                ErrorPayload = reason,
                EndedAt = now,
                LastModifyTime = now
            };
            await _agentStepRepository.UpdateAsync(pendingStep, cancellationToken);
        }

        agentRun = agentRun with
        {
            Status = "Cancelled",
            CurrentWaitToken = string.Empty,
            InterruptReason = reason,
            EndedAt = now,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = now
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
        _agentMetrics.RecordRunCompleted(agentRun.AgentCode, agentRun.Status, agentRun.TotalCost);
        await PublishCancelledEventAsync(agentRun, reason, cancellationToken);

        return ToResult(agentRun);
    }

    private static CancelAgentRunResult ToResult(AgentRun agentRun)
    {
        return new CancelAgentRunResult(
            agentRun.RunId,
            agentRun.AgentCode,
            agentRun.InputPayload,
            agentRun.OutputPayload,
            agentRun.Status,
            string.IsNullOrWhiteSpace(agentRun.CurrentWaitToken) ? null : agentRun.CurrentWaitToken,
            string.IsNullOrWhiteSpace(agentRun.InterruptReason) ? null : agentRun.InterruptReason);
    }

    private static string NormalizeReason(string? reason)
    {
        var normalized = string.IsNullOrWhiteSpace(reason)
            ? "Cancelled by request."
            : reason.Trim();

        return normalized[..Math.Min(normalized.Length, 256)];
    }

    private async Task PublishCancelledEventAsync(AgentRun agentRun, string reason, CancellationToken cancellationToken)
    {
        var data = new AgentRunEventData(
            agentRun.CurrentStepNo,
            "cancelled",
            "Cancelled",
            Error: reason);
        var now = DateTime.UtcNow;
        var nextSeqNo = await _runOutboxEventRepository.GetNextSeqNoAsync(agentRun.RunId, cancellationToken);
        var eventId = Guid.NewGuid().ToString("N");
        var evt = new AgentRunEvent(agentRun.RunId, "cancelled", data, eventId, nextSeqNo, agentRun.Status, now);

        await _runOutboxEventRepository.AddAsync(
            new RunOutboxEvent
            {
                EventId = eventId,
                RunId = agentRun.RunId,
                SeqNo = nextSeqNo,
                EventType = evt.EventType,
                RunStatus = agentRun.Status,
                Payload = JsonSerializer.Serialize(evt.Data),
                PublishStatus = "pending",
                OccurredAt = now,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            },
            cancellationToken);
        _agentRunEventBus.Publish(evt);
        await _runOutboxEventRepository.MarkPublishedAsync(eventId, DateTime.UtcNow, cancellationToken);
    }
}
