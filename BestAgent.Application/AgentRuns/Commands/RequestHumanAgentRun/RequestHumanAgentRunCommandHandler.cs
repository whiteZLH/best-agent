using BestAgent.Application.Exceptions;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Commands.RequestHumanAgentRun;

public class RequestHumanAgentRunCommandHandler : IRequestHandler<RequestHumanAgentRunCommand, RequestHumanAgentRunResult>
{
    private const string SupersededByHumanTakeover = "Superseded by human takeover request.";

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Running",
        "WaitingTool",
        "WaitingApproval"
    };

    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentApprovalRepository _agentApprovalRepository;
    private readonly IToolInvocationRepository _toolInvocationRepository;
    private readonly IRunOutboxEventRepository _runOutboxEventRepository;
    private readonly IAgentRunEventBus _agentRunEventBus;
    private readonly IHumanTakeoverAuthorizer _humanTakeoverAuthorizer;

    public RequestHumanAgentRunCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentApprovalRepository agentApprovalRepository,
        IToolInvocationRepository toolInvocationRepository,
        IRunOutboxEventRepository runOutboxEventRepository,
        IAgentRunEventBus agentRunEventBus,
        IHumanTakeoverAuthorizer humanTakeoverAuthorizer)
    {
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentApprovalRepository = agentApprovalRepository;
        _toolInvocationRepository = toolInvocationRepository;
        _runOutboxEventRepository = runOutboxEventRepository;
        _agentRunEventBus = agentRunEventBus;
        _humanTakeoverAuthorizer = humanTakeoverAuthorizer;
    }

    public async Task<RequestHumanAgentRunResult> Handle(RequestHumanAgentRunCommand request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
        {
            throw new NotFoundException("AgentRun", request.RunId);
        }

        if (!AllowedStatuses.Contains(agentRun.Status))
        {
            throw new ConflictException($"Run '{request.RunId}' is in status '{agentRun.Status}', cannot request human takeover.");
        }

        _humanTakeoverAuthorizer.Authorize(new HumanTakeoverAuthorizationContext(
            request.RunId,
            request.HumanOperatorId,
            request.HumanOperatorName,
            request.HumanOperatorRole));

        var now = DateTime.UtcNow;
        var waitToken = Guid.NewGuid().ToString("N");
        var comment = NormalizeComment(request.Comment);
        var targetStep = await ResolveTargetStepAsync(request.RunId, request.SourceStepId, cancellationToken);
        var humanPayload = await BuildPendingHumanPayloadAsync(
            agentRun,
            targetStep,
            comment,
            request.HumanOperatorId,
            request.HumanOperatorName,
            request.HumanOperatorRole,
            now,
            cancellationToken);
        if (targetStep is not null && string.Equals(targetStep.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            targetStep = targetStep with
            {
                Status = "Cancelled",
                ErrorPayload = SupersededByHumanTakeover,
                EndedAt = now,
                LastModifyTime = now
            };
            await _agentStepRepository.UpdateAsync(targetStep, cancellationToken);
        }

        var nextStepNo = agentRun.CurrentStepNo + 1;
        var humanStep = AgentRunLoop.CreateStep(
            agentRun.RunId,
            nextStepNo,
            "human_wait",
            "Pending",
            comment,
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(humanPayload)
        };
        await _agentStepRepository.AddAsync(humanStep, cancellationToken);

        agentRun = agentRun with
        {
            Status = "WaitingHuman",
            CurrentWaitToken = waitToken,
            CurrentStepNo = nextStepNo,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = now
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
        await PublishWaitingHumanEventAsync(agentRun, humanStep, comment, cancellationToken);

        return new RequestHumanAgentRunResult(
            agentRun.RunId,
            agentRun.AgentCode,
            agentRun.InputPayload,
            agentRun.OutputPayload,
            agentRun.Status,
            agentRun.CurrentWaitToken);
    }

    private async Task<AgentStep?> ResolveTargetStepAsync(
        string runId,
        string? sourceStepId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceStepId))
        {
            return await _agentStepRepository.GetLastByRunIdAsync(runId, cancellationToken);
        }

        var targetStep = await _agentStepRepository.GetByStepIdAsync(sourceStepId.Trim(), cancellationToken);
        if (targetStep is null || !string.Equals(targetStep.RunId, runId, StringComparison.Ordinal))
        {
            throw new NotFoundException("AgentStep", sourceStepId.Trim());
        }

        return targetStep;
    }

    private static string? NormalizeComment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, 256)];
    }

    private async Task<HumanApprovalPayload> BuildPendingHumanPayloadAsync(
        AgentRun agentRun,
        AgentStep? targetStep,
        string? comment,
        string? humanOperatorId,
        string? humanOperatorName,
        string? humanOperatorRole,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (targetStep is null)
        {
            return HumanApprovalPayloadSerializer.CreatePending(comment, sourceType: "run");
        }

        if (!string.IsNullOrWhiteSpace(targetStep.StepId)
            && !string.IsNullOrWhiteSpace(targetStep.OutputPayload)
            && string.Equals(targetStep.StepType, "tool_call", StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetStep.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureLatestCompletedToolStepAsync(agentRun.RunId, targetStep, cancellationToken);
            EnsureTargetStepIsCurrentStep(agentRun, targetStep);
            var completedInvocation = await ResolveLatestInvocationAsync(agentRun.RunId, targetStep.StepId, cancellationToken);
            return HumanApprovalPayloadSerializer.CreatePending(
                comment,
                sourceType: "tool_result",
                sourceStepId: targetStep.StepId,
                sourceInvocationId: completedInvocation?.InvocationId,
                sourceToolName: completedInvocation?.ToolName ?? ExtractToolName(targetStep),
                sourceToolInput: completedInvocation?.InputPayload ?? targetStep.InputPayload,
                sourceToolOutput: completedInvocation?.OutputPayload ?? targetStep.OutputPayload,
                sourceToolStatus: completedInvocation?.Status ?? targetStep.Status,
                continueAsToolResult: true);
        }

        if (!string.Equals(targetStep.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Step '{targetStep.StepId}' is not pending and cannot be taken over by a human operator.");
        }

        if (string.Equals(agentRun.Status, "WaitingTool", StringComparison.OrdinalIgnoreCase))
        {
            var pendingInvocation = await _toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(
                agentRun.RunId,
                targetStep.StepId,
                cancellationToken);
            if (pendingInvocation is null)
            {
                throw new ConflictException($"Run '{agentRun.RunId}' is waiting for tool completion but the pending invocation was not found.");
            }

            pendingInvocation = pendingInvocation with
            {
                Status = "Cancelled",
                ErrorPayload = SupersededByHumanTakeover,
                EndedAt = now,
                DurationMs = pendingInvocation.StartedAt is null
                    ? 0
                    : Math.Max(0, (long)(now - pendingInvocation.StartedAt.Value).TotalMilliseconds),
                LastModifyTime = now
            };
            await _toolInvocationRepository.UpdateAsync(pendingInvocation, cancellationToken);

            return HumanApprovalPayloadSerializer.CreatePending(
                comment,
                sourceType: "tool_wait",
                sourceStepId: targetStep.StepId,
                sourceInvocationId: pendingInvocation.InvocationId,
                sourceToolName: pendingInvocation.ToolName,
                sourceToolInput: pendingInvocation.InputPayload ?? targetStep.InputPayload,
                sourceToolOutput: pendingInvocation.OutputPayload,
                sourceToolStatus: pendingInvocation.Status,
                continueAsToolResult: true);
        }

        if (string.Equals(agentRun.Status, "WaitingApproval", StringComparison.OrdinalIgnoreCase))
        {
            var approvalRecord = await _agentApprovalRepository.GetByRunIdAndStepIdAsync(
                agentRun.RunId,
                targetStep.StepId,
                cancellationToken);
            if (approvalRecord is not null && string.Equals(approvalRecord.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
            {
                approvalRecord = approvalRecord with
                {
                    Decision = ApprovalDecisions.Rejected,
                    ApproverId = Normalize(humanOperatorId) ?? "system",
                    ApproverName = Normalize(humanOperatorName) ?? Normalize(humanOperatorId) ?? "system",
                    ApproverRole = Normalize(humanOperatorRole) ?? string.Empty,
                    Comment = SupersededByHumanTakeover,
                    DecidedAt = now,
                    LastModifier = Normalize(humanOperatorId) ?? "system",
                    LastModifierName = Normalize(humanOperatorName) ?? Normalize(humanOperatorId) ?? "system",
                    LastModifyTime = now
                };
                await _agentApprovalRepository.UpdateAsync(approvalRecord, cancellationToken);
            }

            PendingApprovalContextParser.TryParsePending(targetStep, out var approvalContext);
            return HumanApprovalPayloadSerializer.CreatePending(
                comment,
                sourceType: "approval_wait",
                sourceStepId: targetStep.StepId,
                sourceToolName: approvalRecord?.RequestedAction ?? approvalContext?.RequestedAction,
                sourceToolInput: approvalRecord?.RequestPayload ?? approvalContext?.RequestPayload,
                sourceToolOutput: null,
                sourceToolStatus: targetStep.Status,
                continueAsToolResult: false);
        }

        return HumanApprovalPayloadSerializer.CreatePending(comment, sourceType: "run");
    }

    private async Task EnsureLatestCompletedToolStepAsync(
        string runId,
        AgentStep targetStep,
        CancellationToken cancellationToken)
    {
        var steps = await _agentStepRepository.ListByRunIdAsync(runId, cancellationToken);
        var latestCompletedToolStep = steps
            .Where(step =>
                string.Equals(step.StepType, "tool_call", StringComparison.OrdinalIgnoreCase)
                && string.Equals(step.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(step.OutputPayload))
            .OrderByDescending(step => step.StepNo)
            .ThenByDescending(step => step.LastModifyTime)
            .FirstOrDefault();

        if (latestCompletedToolStep is null
            || !string.Equals(latestCompletedToolStep.StepId, targetStep.StepId, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"Step '{targetStep.StepId}' is not the latest completed tool result for run '{runId}' and cannot be overridden by human takeover.");
        }
    }

    private static void EnsureTargetStepIsCurrentStep(
        AgentRun agentRun,
        AgentStep targetStep)
    {
        if (agentRun.CurrentStepNo != targetStep.StepNo)
        {
            throw new ConflictException(
                $"Step '{targetStep.StepId}' is no longer the current step for run '{agentRun.RunId}' and cannot be overridden by human takeover.");
        }
    }

    private async Task<ToolInvocation?> ResolveLatestInvocationAsync(
        string runId,
        string stepId,
        CancellationToken cancellationToken)
    {
        var invocations = await _toolInvocationRepository.ListByRunIdAsync(runId, cancellationToken);
        return invocations
            .Where(invocation => string.Equals(invocation.StepId, stepId, StringComparison.Ordinal))
            .OrderByDescending(invocation => invocation.CreateTime)
            .FirstOrDefault();
    }

    private static string ExtractToolName(AgentStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.DecisionPayload))
        {
            try
            {
                using var document = JsonDocument.Parse(step.DecisionPayload);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("toolName", out var toolNameProperty)
                    && toolNameProperty.ValueKind == JsonValueKind.String)
                {
                    var toolName = toolNameProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(toolName))
                    {
                        return toolName.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                // Best effort only.
            }

            if (ApprovalPayloadSerializer.TryParse(step.DecisionPayload, out var approvalPayload)
                && !string.IsNullOrWhiteSpace(approvalPayload?.ToolName))
            {
                return approvalPayload.ToolName;
            }
        }

        return "tool_call";
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task PublishWaitingHumanEventAsync(
        AgentRun agentRun,
        AgentStep humanStep,
        string? comment,
        CancellationToken cancellationToken)
    {
        var data = new AgentRunEventData(humanStep.StepNo, humanStep.StepType, "Pending", comment);
        var now = DateTime.UtcNow;
        var nextSeqNo = await _runOutboxEventRepository.GetNextSeqNoAsync(agentRun.RunId, cancellationToken);
        var eventId = Guid.NewGuid().ToString("N");
        var evt = new AgentRunEvent(agentRun.RunId, "waiting_human", data, eventId, nextSeqNo, agentRun.Status, now);

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
