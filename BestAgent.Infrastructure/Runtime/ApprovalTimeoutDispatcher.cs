using System.Text.Json;
using System.Diagnostics;
using BestAgent.Application.Observability;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BestAgent.Infrastructure.Runtime;

public class ApprovalTimeoutDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApprovalTimeoutDispatcher> _logger;
    private readonly ApprovalTimeoutOptions _options;
    private readonly IAgentMetrics _agentMetrics;

    public ApprovalTimeoutDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<ApprovalTimeoutDispatcher> logger,
        ApprovalTimeoutOptions? options = null,
        IAgentMetrics? agentMetrics = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options ?? new ApprovalTimeoutOptions();
        _agentMetrics = agentMetrics ?? NullAgentMetrics.Instance;
    }

    public async Task<int> DispatchExpiredAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_options.TimeoutMinutes <= 0 || _options.BatchSize <= 0)
        {
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var approvalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();
        var runRepository = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var stepRepository = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IRunOutboxEventRepository>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IAgentRunEventBus>();

        var expiredApprovals = await approvalRepository.ListExpiredPendingAsync(utcNow, _options.BatchSize, cancellationToken);
        var processed = 0;

        foreach (var approvalRecord in expiredApprovals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var activity = AgentTracing.Source.StartActivity(AgentTracing.ApprovalActivityName, ActivityKind.Internal);
            activity?.SetTag("bestagent.run_id", approvalRecord.RunId);
            activity?.SetTag("bestagent.step_id", approvalRecord.StepId);
            activity?.SetTag("bestagent.approval_action", "timeout");

            try
            {
                var approval = approvalRecord;
                var run = await runRepository.GetByRunIdAsync(approval.RunId, cancellationToken);
                if (run is null
                    || !string.Equals(run.Status, "WaitingApproval", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(run.CurrentWaitToken, approval.WaitToken, StringComparison.Ordinal))
                {
                    continue;
                }

                var pendingStep = await stepRepository.GetLastByRunIdAsync(approval.RunId, cancellationToken);
                if (pendingStep is null
                    || !string.Equals(pendingStep.StepId, approval.StepId, StringComparison.Ordinal)
                    || !string.Equals(pendingStep.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var timeoutReason = NormalizeTimeoutComment(_options.TimeoutComment);
                string serializedRejectedPayload;
                if (string.Equals(pendingStep.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
                    && HandoffPayloadSerializer.TryParse(pendingStep.DecisionPayload, out var handoffPayload)
                    && handoffPayload!.ApprovalRequired
                    && string.Equals(handoffPayload.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
                {
                    serializedRejectedPayload = HandoffPayloadSerializer.Serialize(
                        HandoffPayloadSerializer.MarkRejected(handoffPayload, timeoutReason));
                }
                else if (ApprovalPayloadSerializer.TryParse(pendingStep.DecisionPayload, out var approvalPayload)
                    && string.Equals(approvalPayload!.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
                {
                    serializedRejectedPayload = ApprovalPayloadSerializer.Serialize(
                        ApprovalPayloadSerializer.MarkRejected(approvalPayload, timeoutReason));
                }
                else
                {
                    continue;
                }

                approval = approval with
                {
                    Decision = ApprovalDecisions.Rejected,
                    ApproverId = "system",
                    ApproverName = "system",
                    ApproverRole = string.Empty,
                    Comment = timeoutReason,
                    DecidedAt = utcNow,
                    LastModifier = "system",
                    LastModifierName = "system",
                    LastModifyTime = utcNow
                };
                await approvalRepository.UpdateAsync(approval, cancellationToken);

                pendingStep = pendingStep with
                {
                    Status = "Failed",
                    ErrorPayload = timeoutReason,
                    DecisionPayload = serializedRejectedPayload,
                    EndedAt = utcNow,
                    LastModifyTime = utcNow
                };
                await stepRepository.UpdateAsync(pendingStep, cancellationToken);

                run = run with
                {
                    Status = "TimedOut",
                    CurrentWaitToken = string.Empty,
                    InterruptReason = timeoutReason,
                    EndedAt = utcNow,
                    StatusVersion = run.StatusVersion + 1,
                    LastModifyTime = utcNow
                };
                await runRepository.UpdateAsync(run, cancellationToken);
                var waitDuration = utcNow >= approval.CreateTime
                    ? utcNow - approval.CreateTime
                    : TimeSpan.Zero;
                _agentMetrics.RecordApprovalTimedOut(run.AgentCode, pendingStep.StepType, waitDuration);
                _agentMetrics.RecordRunCompleted(run.AgentCode, run.Status, run.TotalCost);
                activity?.SetTag("bestagent.step_type", pendingStep.StepType);
                activity?.SetTag("bestagent.status", "timedout");
                activity?.SetStatus(ActivityStatusCode.Ok);

                await PublishEventAsync(
                    outboxRepository,
                    eventBus,
                    approval.RunId,
                    "approval_timed_out",
                    "TimedOut",
                    new AgentRunEventData(pendingStep.StepNo, pendingStep.StepType, "TimedOut", Error: timeoutReason),
                    utcNow,
                    cancellationToken);
                await PublishEventAsync(
                    outboxRepository,
                    eventBus,
                    approval.RunId,
                    "error",
                    "TimedOut",
                    new AgentRunEventData(pendingStep.StepNo, pendingStep.StepType, "TimedOut", Error: timeoutReason),
                    utcNow,
                    cancellationToken);

                processed++;
            }
            catch (Exception ex)
            {
                activity?.SetTag("error.type", ex.GetType().Name);
                activity?.SetTag("error.message", ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogWarning(ex, "Failed to timeout approval {ApprovalId} for run {RunId}", approvalRecord.ApprovalId, approvalRecord.RunId);
            }
        }

        return processed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchExpiredAsync(DateTime.UtcNow, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds > 0 ? _options.PollIntervalSeconds : 5), stoppingToken);
        }
    }

    private static string NormalizeTimeoutComment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Approval timed out.";
        }

        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, 256)];
    }

    private static async Task PublishEventAsync(
        IRunOutboxEventRepository outboxRepository,
        IAgentRunEventBus eventBus,
        string runId,
        string eventType,
        string runStatus,
        AgentRunEventData data,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var nextSeqNo = await outboxRepository.GetNextSeqNoAsync(runId, cancellationToken);
        var evt = new AgentRunEvent(runId, eventType, data, eventId, nextSeqNo, runStatus, now);

        await outboxRepository.AddAsync(
            new RunOutboxEvent
            {
                EventId = eventId,
                RunId = runId,
                SeqNo = nextSeqNo,
                EventType = eventType,
                RunStatus = runStatus,
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

        eventBus.Publish(evt);
        await outboxRepository.MarkPublishedAsync(eventId, now, cancellationToken);
    }
}
