using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Api.Infrastructure;
using BestAgent.Application.AgentRuns.Commands.CancelAgentRun;
using BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.CompleteHumanAgentRun;
using BestAgent.Application.AgentRuns.Commands.CompleteApproval;
using BestAgent.Application.AgentRuns.Commands.CompleteToolInvocation;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.RequestHumanAgentRun;
using BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunTree;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BestAgent.Api.Controllers;

[ApiController]
[Route("agent-runs")]
public class AgentRunsController : ControllerBase
{
    private const string TenantScopeHeaderName = "X-BestAgent-Tenant-Id";
    private const string UserScopeHeaderName = "X-BestAgent-User-Id";
    private const string SessionScopeHeaderName = "X-BestAgent-Session-Id";

    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly IAgentRunEventBus _eventBus;
    private readonly IWebhookRequestAuthorizer _webhookRequestAuthorizer;
    private readonly IToolInvocationRepository? _toolInvocationRepository;
    private readonly IToolDefinitionRepository? _toolDefinitionRepository;
    private readonly IAgentRunRepository? _agentRunRepository;
    private readonly BestAgentAuthenticationOptions _authenticationOptions;
    private readonly IAgentMetrics _agentMetrics;

    public AgentRunsController(
        IMediator mediator,
        IMapper mapper,
        IAgentRunEventBus eventBus,
        IWebhookRequestAuthorizer? webhookRequestAuthorizer = null,
        IToolInvocationRepository? toolInvocationRepository = null,
        IToolDefinitionRepository? toolDefinitionRepository = null,
        IAgentRunRepository? agentRunRepository = null,
        BestAgentAuthenticationOptions? authenticationOptions = null,
        IAgentMetrics? agentMetrics = null)
    {
        _mediator = mediator;
        _mapper = mapper;
        _eventBus = eventBus;
        _webhookRequestAuthorizer = webhookRequestAuthorizer ?? new HmacWebhookRequestAuthorizer(new WebhookSecurityOptions());
        _toolInvocationRepository = toolInvocationRepository;
        _toolDefinitionRepository = toolDefinitionRepository;
        _agentRunRepository = agentRunRepository;
        _authenticationOptions = authenticationOptions ?? new BestAgentAuthenticationOptions();
        _agentMetrics = agentMetrics ?? NullAgentMetrics.Instance;
    }

    [HttpPost]
    public async Task<ActionResult<CreateAgentRunResponse>> Create(
        [FromBody] CreateAgentRunRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        var command = _mapper.Map<CreateAgentRunCommand>(request);
        command = ApplyAuthenticatedRunIdentity(command);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            command = command with { IdempotencyKey = idempotencyKey.Trim() };
        }

        var result = await _mediator.Send(command, cancellationToken);
        var response = _mapper.Map<CreateAgentRunResponse>(result);
        if (command.Stream == true)
        {
            response = response with
            {
                StreamUrl = $"/agent-runs/{response.RunId}/stream"
            };
        }

        return Created($"/agent-runs/{response.RunId}", response);
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<GetAgentRunResponse>> GetById(
        [FromRoute] string runId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var result = await _mediator.Send(new GetAgentRunByIdQuery(runId), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        var response = _mapper.Map<GetAgentRunResponse>(result) with
        {
            StreamUrl = $"/agent-runs/{runId}/stream"
        };

        return Ok(response);
    }

    [HttpGet("{runId}/children")]
    public async Task<ActionResult<IReadOnlyList<GetAgentRunChildResponse>>> GetChildren(
        [FromRoute] string runId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var children = await _mediator.Send(new GetAgentRunChildrenQuery(runId), cancellationToken);
        var response = _mapper.Map<IReadOnlyList<GetAgentRunChildResponse>>(children)
            .Select(AttachStreamUrl)
            .ToArray();
        return Ok(response);
    }

    [HttpGet("{runId}/tree")]
    public async Task<ActionResult<GetAgentRunTreeResponse>> GetTree(
        [FromRoute] string runId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var tree = await _mediator.Send(new GetAgentRunTreeQuery(runId), cancellationToken);
        if (tree is null)
        {
            return NotFound();
        }

        var response = AttachStreamUrl(_mapper.Map<GetAgentRunTreeResponse>(tree));
        return Ok(response);
    }

    [HttpGet("{runId}/steps")]
    public async Task<ActionResult<IReadOnlyList<GetAgentRunStepResponse>>> GetSteps(
        [FromRoute] string runId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var steps = await _mediator.Send(new GetAgentRunStepsQuery(runId), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetAgentRunStepResponse>>(steps));
    }

    [HttpGet("{runId}/approvals")]
    public async Task<ActionResult<IReadOnlyList<GetAgentRunApprovalResponse>>> GetApprovals(
        [FromRoute] string runId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var approvals = await _mediator.Send(new GetAgentRunApprovalsQuery(runId), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetAgentRunApprovalResponse>>(approvals));
    }

    [HttpGet("{runId}/events")]
    public async Task<ActionResult<IReadOnlyList<GetAgentRunEventResponse>>> GetEvents(
        [FromRoute] string runId,
        [FromQuery] long? afterSeqNo,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var events = await _mediator.Send(new GetAgentRunEventsQuery(runId, afterSeqNo), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetAgentRunEventResponse>>(events));
    }

    [HttpPost("{runId}:resume")]
    public async Task<ActionResult<ResumeAgentRunResponse>> Resume(
        [FromRoute] string runId,
        [FromBody] ResumeAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var command = new ResumeAgentRunCommand(runId, request.WaitToken, request.ToolResult);
        var result = await _mediator.Send(command, cancellationToken);
        var response = _mapper.Map<ResumeAgentRunResponse>(result);

        return Ok(response);
    }

    [HttpPost("{runId}/tool-invocations/{invocationId}:complete")]
    public async Task<ActionResult<CompleteToolInvocationResponse>> CompleteToolInvocation(
        [FromRoute] string runId,
        [FromRoute] string invocationId,
        [FromBody] CompleteToolInvocationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await EnsureRunAccessAsync(runId, cancellationToken);
        var callbackSecret = await ResolveToolCallbackSecretAsync(invocationId, cancellationToken);
        await _webhookRequestAuthorizer.AuthorizeToolCallbackAsync(Request, callbackSecret, cancellationToken);
        var result = await _mediator.Send(
            new CompleteToolInvocationCommand(runId, invocationId, request.WaitToken, request.ToolResult, idempotencyKey),
            cancellationToken);
        var response = _mapper.Map<CompleteToolInvocationResponse>(result);

        return Ok(response);
    }

    [HttpPost("{runId}/approvals/{approvalId}:complete")]
    public async Task<ActionResult<CompleteApprovalResponse>> CompleteApproval(
        [FromRoute] string runId,
        [FromRoute] string approvalId,
        [FromBody] CompleteApprovalRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await EnsureRunAccessAsync(runId, cancellationToken);
        await _webhookRequestAuthorizer.AuthorizeApprovalCallbackAsync(Request, cancellationToken);
        var actor = ResolveApprovalActor(request.ApproverId, request.ApproverName, request.ApproverRole);
        var result = await _mediator.Send(
            new CompleteApprovalCommand(
                runId,
                approvalId,
                request.Decision,
                actor.ApproverId,
                actor.ApproverName,
                actor.ApproverRole,
                request.Comment,
                idempotencyKey),
            cancellationToken);
        var response = _mapper.Map<CompleteApprovalResponse>(result);

        return Ok(response);
    }

    [HttpPost("{runId}:cancel")]
    public async Task<ActionResult<CancelAgentRunResponse>> Cancel(
        [FromRoute] string runId,
        [FromBody] CancelAgentRunRequest? request,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var result = await _mediator.Send(new CancelAgentRunCommand(runId, request?.Reason), cancellationToken);
        var response = _mapper.Map<CancelAgentRunResponse>(result);

        return Ok(response);
    }

    [HttpPost("{runId}:request-human")]
    public async Task<ActionResult<RequestHumanAgentRunResponse>> RequestHuman(
        [FromRoute] string runId,
        [FromBody] RequestHumanAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var actor = ResolveApprovalActor(request.HumanOperatorId, request.HumanOperatorName, request.HumanOperatorRole);
        var result = await _mediator.Send(
            new RequestHumanAgentRunCommand(
                runId,
                request.Comment,
                request.SourceStepId,
                actor.ApproverId,
                actor.ApproverName,
                actor.ApproverRole),
            cancellationToken);
        var response = _mapper.Map<RequestHumanAgentRunResponse>(result);

        return Ok(response);
    }

    [HttpPost("{runId}/steps/{stepId}:complete-human")]
    public async Task<ActionResult<CompleteHumanAgentRunResponse>> CompleteHuman(
        [FromRoute] string runId,
        [FromRoute] string stepId,
        [FromBody] CompleteHumanAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var actor = ResolveApprovalActor(request.HumanOperatorId, request.HumanOperatorName, request.HumanOperatorRole);
        var result = await _mediator.Send(
            new CompleteHumanAgentRunCommand(
                runId,
                stepId,
                request.WaitToken,
                request.HumanResult,
                request.Comment,
                request.Terminate,
                actor.ApproverId,
                actor.ApproverName,
                actor.ApproverRole),
            cancellationToken);
        var response = _mapper.Map<CompleteHumanAgentRunResponse>(result);

        return Ok(response);
    }

    [HttpPost("{runId}/steps/{stepId}:approve")]
    public async Task<ActionResult<ApproveAgentRunStepResponse>> Approve(
        [FromRoute] string runId,
        [FromRoute] string stepId,
        [FromBody] ApproveAgentRunStepRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var actor = ResolveApprovalActor(request.ApproverId, request.ApproverName, request.ApproverRole);
        var result = await _mediator.Send(
            new ApproveAgentRunStepCommand(
                runId,
                stepId,
                actor.ApproverId,
                actor.ApproverName,
                actor.ApproverRole,
                request.Comment),
            cancellationToken);
        var response = _mapper.Map<ApproveAgentRunStepResponse>(result);

        return Ok(response);
    }

    [HttpPost("{runId}/steps/{stepId}:reject")]
    public async Task<ActionResult<RejectAgentRunStepResponse>> Reject(
        [FromRoute] string runId,
        [FromRoute] string stepId,
        [FromBody] RejectAgentRunStepRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        var actor = ResolveApprovalActor(request.ApproverId, request.ApproverName, request.ApproverRole);
        var result = await _mediator.Send(
            new RejectAgentRunStepCommand(
                runId,
                stepId,
                request.Comment,
                actor.ApproverId,
                actor.ApproverName,
                actor.ApproverRole),
            cancellationToken);
        var response = _mapper.Map<RejectAgentRunStepResponse>(result);

        return Ok(response);
    }

    [HttpGet("{runId}/stream")]
    public async Task Stream([FromRoute] string runId, CancellationToken cancellationToken)
    {
        EnsureAuthenticatedRunAccess();
        await EnsureRunAccessAsync(runId, cancellationToken);
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        var startedAt = DateTime.UtcNow;
        var replayAfterSeqNo = TryParseLastEventId(Request.Headers["Last-Event-ID"]);
        var replayRequested = replayAfterSeqNo.HasValue;
        var replayDeliveredCount = 0;
        var liveDeliveredCount = 0;
        var completionReason = "completed";
        _agentMetrics.RecordRunStreamOpened(replayRequested);

        using var activity = AgentTracing.Source.StartActivity(AgentTracing.RunStreamActivityName, ActivityKind.Server);
        activity?.SetTag("bestagent.run_id", runId);
        activity?.SetTag("bestagent.stream_replay_requested", replayRequested);
        if (replayAfterSeqNo.HasValue)
        {
            activity?.SetTag("bestagent.stream_last_event_id", replayAfterSeqNo.Value);
        }

        await using var subscription = _eventBus.Subscribe(runId);

        long? lastDeliveredSeqNo = replayAfterSeqNo;
        try
        {
            if (replayAfterSeqNo.HasValue)
            {
                var replayEvents = await _mediator.Send(
                    new GetAgentRunEventsQuery(runId, replayAfterSeqNo.Value),
                    cancellationToken);

                foreach (var replayEvent in replayEvents)
                {
                    await WriteSseEventAsync(
                        replayEvent.EventType,
                        replayEvent.SeqNo,
                        BuildStreamEventResponse(replayEvent),
                        cancellationToken);
                    replayDeliveredCount++;
                    lastDeliveredSeqNo = replayEvent.SeqNo;
                    RecordStreamEvent(activity, replayEvent.EventType, replayEvent.SeqNo, replay: true);
                }

                if (replayEvents.Count > 0 && IsTerminalEventType(replayEvents[^1].EventType))
                {
                    completionReason = "completed_terminal_replay";
                    return;
                }
            }

            await foreach (var evt in subscription.ReadAllAsync(cancellationToken))
            {
                if (lastDeliveredSeqNo.HasValue
                    && evt.SeqNo.HasValue
                    && evt.SeqNo.Value <= lastDeliveredSeqNo.Value)
                {
                    continue;
                }

                await WriteSseEventAsync(
                    evt.EventType,
                    evt.SeqNo,
                    BuildStreamEventResponse(evt),
                    cancellationToken);
                liveDeliveredCount++;
                lastDeliveredSeqNo = evt.SeqNo ?? lastDeliveredSeqNo;
                RecordStreamEvent(activity, evt.EventType, evt.SeqNo, replay: false);
                if (IsTerminalEventType(evt.EventType))
                {
                    completionReason = "completed_terminal_live";
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completionReason = "cancelled";
            activity?.SetTag("bestagent.stream_cancelled", true);
            throw;
        }
        catch
        {
            completionReason = "failed";
            activity?.SetTag("bestagent.stream_failed", true);
            throw;
        }
        finally
        {
            var deliveredCount = replayDeliveredCount + liveDeliveredCount;
            activity?.SetTag("bestagent.stream_replay_delivered_count", replayDeliveredCount);
            activity?.SetTag("bestagent.stream_live_delivered_count", liveDeliveredCount);
            activity?.SetTag("bestagent.stream_delivered_count", deliveredCount);
            activity?.SetTag("bestagent.stream_completion_reason", completionReason);
            _agentMetrics.RecordRunStreamCompleted(
                completionReason,
                deliveredCount,
                DateTime.UtcNow - startedAt);
        }
    }

    private static StreamAgentRunEventResponse BuildStreamEventResponse(AgentRunEvent evt)
    {
        var maskedData = RuntimeEventPayloadMasker.MaskEventData(evt.Data);
        return new StreamAgentRunEventResponse(
            evt.EventId,
            evt.RunId,
            evt.SeqNo,
            evt.EventType,
            evt.RunStatus,
            evt.OccurredAt,
            BuildEventDataResponse(
                BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents.EventDataInfo.FromRuntimeData(maskedData),
                null,
                evt.RunStatus ?? maskedData.Status));
    }

    private static StreamAgentRunEventResponse BuildStreamEventResponse(GetAgentRunEventsItem evt)
    {
        return new StreamAgentRunEventResponse(
            evt.EventId,
            evt.RunId,
            evt.SeqNo,
            evt.EventType,
            evt.RunStatus,
            evt.OccurredAt,
            BuildEventDataResponse(evt.Data, evt.Payload, evt.RunStatus));
    }

    private static EventDataInfoResponse BuildEventDataResponse(
        BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents.EventDataInfo? data,
        string? payload,
        string runStatus)
    {
        var resolvedData = data
            ?? BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents.EventDataInfo.FromPayload(payload)
            ?? new BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents.EventDataInfo(
                0,
                "unknown",
                runStatus,
                null,
                null,
                null,
                null,
                null,
                null);

        return new EventDataInfoResponse(
            resolvedData.StepNo,
            resolvedData.StepType,
            resolvedData.Status,
            resolvedData.Output,
            resolvedData.Error,
            resolvedData.ModelCall is null
                ? null
                : new EventModelCallInfoResponse(
                    resolvedData.ModelCall.Model,
                    resolvedData.ModelCall.ResponseId,
                    resolvedData.ModelCall.PromptTokens,
                    resolvedData.ModelCall.CompletionTokens,
                    resolvedData.ModelCall.TotalTokens,
                    resolvedData.ModelCall.Cost,
                    resolvedData.ModelCall.Retrieval is null
                        ? null
                        : new EventModelCallRetrievalInfoResponse(
                            resolvedData.ModelCall.Retrieval.QueryText,
                            resolvedData.ModelCall.Retrieval.WasRewritten,
                            resolvedData.ModelCall.Retrieval.CandidateCount,
                            resolvedData.ModelCall.Retrieval.SelectedCount,
                            resolvedData.ModelCall.Retrieval.RequestedSources,
                            resolvedData.ModelCall.Retrieval.SelectedSources,
                            resolvedData.ModelCall.Retrieval.Citations),
                    resolvedData.ModelCall.FinishReason,
                    resolvedData.ModelCall.ServiceTier,
                    resolvedData.ModelCall.ReasoningSummary,
                    resolvedData.ModelCall.ToolCalls?
                        .Select(toolCall => new EventModelCallToolCallInfoResponse(
                            toolCall.Id,
                            toolCall.Type,
                            toolCall.Name,
                            toolCall.Arguments))
                        .ToArray()),
            resolvedData.Retrieval is null
                ? null
                : new EventRetrievalInfoResponse(
                    resolvedData.Retrieval.QueryText),
            resolvedData.ModelFailure is null
                ? null
                : new EventModelFailureInfoResponse(
                    resolvedData.ModelFailure.ErrorCode,
                    resolvedData.ModelFailure.Message),
            resolvedData.ToolFailure is null
                ? null
                : new EventToolFailureInfoResponse(
                    resolvedData.ToolFailure.ToolName,
                    resolvedData.ToolFailure.Stage,
                    resolvedData.ToolFailure.Message,
                    resolvedData.ToolFailure.Compensation is null
                        ? null
                        : new EventToolFailureCompensationInfoResponse(
                            resolvedData.ToolFailure.Compensation.Mode)),
            resolvedData.ToolInvocation is null
                ? null
                : new EventToolInvocationInfoResponse(
                    resolvedData.ToolInvocation.InvocationId,
                    resolvedData.ToolInvocation.ToolName,
                    resolvedData.ToolInvocation.Mode,
                    resolvedData.ToolInvocation.Status,
                    resolvedData.ToolInvocation.CallbackToken),
            resolvedData.Approval is null
                ? null
                : new EventApprovalInfoResponse(
                    resolvedData.Approval.WaitType,
                    resolvedData.Approval.RequestedAction,
                    resolvedData.Approval.RequestPayload,
                    resolvedData.Approval.SideEffectLevel,
                    resolvedData.Approval.Decision,
                    resolvedData.Approval.Comment,
                    resolvedData.Approval.DecidedAt),
            resolvedData.Handoff is null
                ? null
                : new EventHandoffInfoResponse(
                    resolvedData.Handoff.WaitType,
                    resolvedData.Handoff.TargetAgent,
                    resolvedData.Handoff.HandoffInput,
                    resolvedData.Handoff.Mode,
                    resolvedData.Handoff.ChildRunId,
                    resolvedData.Handoff.Decision,
                    resolvedData.Handoff.ChildStatus,
                    resolvedData.Handoff.ChildOutput,
                    resolvedData.Handoff.Comment,
                    resolvedData.Handoff.DecidedAt,
                    resolvedData.Handoff.RouteRuleId,
                    resolvedData.Handoff.ContextScope,
                    resolvedData.Handoff.MemoryScope,
                    resolvedData.Handoff.ToolScope,
                    resolvedData.Handoff.KnowledgeScope,
                    resolvedData.Handoff.ApprovalRequired,
                    resolvedData.Handoff.Reason,
                    resolvedData.Handoff.Confidence,
                    resolvedData.Handoff.ContextOverrides,
                    resolvedData.Handoff.MemoryOverrides,
                    resolvedData.Handoff.ToolOverrides,
                    resolvedData.Handoff.KnowledgeOverrides,
                    resolvedData.Handoff.MergeStrategy),
            resolvedData.HumanWait is null
                ? null
                : new EventHumanWaitInfoResponse(
                    resolvedData.HumanWait.WaitType,
                    resolvedData.HumanWait.Decision,
                    resolvedData.HumanWait.Comment,
                    resolvedData.HumanWait.DecidedAt,
                    resolvedData.HumanWait.HumanOperatorId,
                    resolvedData.HumanWait.HumanOperatorName,
                    resolvedData.HumanWait.HumanOperatorRole,
                    resolvedData.HumanWait.HumanResult,
                    resolvedData.HumanWait.SourceType,
                    resolvedData.HumanWait.SourceStepId,
                    resolvedData.HumanWait.SourceInvocationId,
                    resolvedData.HumanWait.SourceToolName,
                    resolvedData.HumanWait.SourceToolInput,
                    resolvedData.HumanWait.SourceToolOutput,
                    resolvedData.HumanWait.SourceToolStatus,
                    resolvedData.HumanWait.ContinueAsToolResult));
    }

    private async Task WriteSseEventAsync(
        string eventType,
        long? seqNo,
        StreamAgentRunEventResponse payload,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (seqNo.HasValue)
        {
            await Response.WriteAsync($"id: {seqNo.Value}\n", cancellationToken);
        }

        await Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private void RecordStreamEvent(Activity? activity, string eventType, long? seqNo, bool replay)
    {
        _agentMetrics.RecordRunStreamEvent(eventType, replay);
        activity?.AddEvent(new ActivityEvent(
            "bestagent.stream.event",
            tags: new ActivityTagsCollection
            {
                { "bestagent.stream_event_type", eventType },
                { "bestagent.stream_event_phase", replay ? "replay" : "live" },
                { "bestagent.stream_event_seq_no", seqNo }
            }));
    }

    private void EnsureAuthenticatedRunAccess()
    {
        var requiresRole = _authenticationOptions.RunAllowedRoles.Length > 0;
        if (!_authenticationOptions.RequireAuthenticatedRunAccess && !requiresRole)
        {
            return;
        }

        if (HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            if (!requiresRole
                || _authenticationOptions.RunAllowedRoles.Any(HttpContext.User.IsInRole))
            {
                return;
            }

            throw new ForbiddenException(
                $"Run access requires one of roles: {string.Join(", ", _authenticationOptions.RunAllowedRoles)}.");
        }

        throw new UnauthorizedException("Authenticated access is required for this run endpoint.");
    }

    private static long? TryParseLastEventId(string? lastEventId)
    {
        if (string.IsNullOrWhiteSpace(lastEventId))
        {
            return null;
        }

        return long.TryParse(lastEventId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seqNo)
            && seqNo >= 0
            ? seqNo
            : null;
    }

    private static bool IsTerminalEventType(string eventType)
    {
        return eventType is "done" or "error" or "cancelled";
    }

    private CreateAgentRunCommand ApplyAuthenticatedRunIdentity(CreateAgentRunCommand command)
    {
        var access = ResolveRunAccessContext();
        return access is null
            ? command
            : command with
            {
                TenantId = FirstNonEmpty(access.TenantId, command.TenantId),
                UserId = FirstNonEmpty(access.UserId, command.UserId),
                SessionId = FirstNonEmpty(access.SessionId, command.SessionId)
            };
    }

    private async Task EnsureRunAccessAsync(string runId, CancellationToken cancellationToken)
    {
        if (_agentRunRepository is null)
        {
            return;
        }

        var access = ResolveRunAccessContext();
        if (access is null)
        {
            return;
        }

        var run = await _agentRunRepository.GetByRunIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        if (!MatchesScope(access.TenantId, run.TenantId)
            || !MatchesScope(access.UserId, run.UserId)
            || !MatchesScope(access.SessionId, run.SessionId))
        {
            throw new ForbiddenException($"Run '{runId}' is outside the authenticated tenant or user scope.");
        }
    }

    private RunAccessContext? ResolveRunAccessContext()
    {
        var user = HttpContext?.User;
        var headers = HttpContext?.Request?.Headers;
        var tenantId = user?.Identity?.IsAuthenticated == true
            ? FirstNonEmpty(
                user.FindFirstValue("tenant_id"),
                user.FindFirstValue("tenantId"),
                user.FindFirstValue("tenant"),
                user.FindFirstValue("tid"))
            : null;
        var userId = user?.Identity?.IsAuthenticated == true
            ? FirstNonEmpty(
                user.FindFirstValue(ClaimTypes.NameIdentifier),
                user.FindFirstValue("sub"),
                user.FindFirstValue(ClaimTypes.Name),
                user.FindFirstValue("name"))
            : null;
        var sessionId = user?.Identity?.IsAuthenticated == true
            ? FirstNonEmpty(
                user.FindFirstValue("session_id"),
                user.FindFirstValue("sessionId"),
                user.FindFirstValue("session"),
                user.FindFirstValue("sid"))
            : null;

        tenantId = FirstNonEmpty(tenantId, headers?[TenantScopeHeaderName]);
        userId = FirstNonEmpty(userId, headers?[UserScopeHeaderName]);
        sessionId = FirstNonEmpty(sessionId, headers?[SessionScopeHeaderName]);

        return string.IsNullOrWhiteSpace(tenantId)
               && string.IsNullOrWhiteSpace(userId)
               && string.IsNullOrWhiteSpace(sessionId)
            ? null
            : new RunAccessContext(tenantId, userId, sessionId);
    }

    private static bool MatchesScope(string? expected, string actual)
    {
        var normalizedExpected = Normalize(expected);
        if (string.IsNullOrWhiteSpace(normalizedExpected))
        {
            return true;
        }

        return string.Equals(normalizedExpected, Normalize(actual), StringComparison.Ordinal);
    }

    private ApprovalActor ResolveApprovalActor(string? fallbackApproverId, string? fallbackApproverName, string? fallbackApproverRole)
    {
        var user = HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var approverId = FirstNonEmpty(
                user.FindFirstValue(ClaimTypes.NameIdentifier),
                user.FindFirstValue("sub"),
                fallbackApproverId);
            var approverName = FirstNonEmpty(
                user.Identity?.Name,
                user.FindFirstValue(ClaimTypes.Name),
                user.FindFirstValue("name"),
                fallbackApproverName,
                approverId);
            var approverRole = FirstNonEmpty(
                user.FindFirstValue(ClaimTypes.Role),
                user.FindFirstValue("role"),
                user.FindFirstValue("roles"),
                fallbackApproverRole);

            return new ApprovalActor(approverId, approverName, approverRole);
        }

        return new ApprovalActor(
            Normalize(fallbackApproverId),
            Normalize(fallbackApproverName),
            Normalize(fallbackApproverRole));
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = Normalize(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static GetAgentRunChildResponse AttachStreamUrl(GetAgentRunChildResponse response)
    {
        return response with
        {
            StreamUrl = $"/agent-runs/{response.RunId}/stream"
        };
    }

    private static GetAgentRunTreeResponse AttachStreamUrl(GetAgentRunTreeResponse response)
    {
        return response with
        {
            StreamUrl = $"/agent-runs/{response.RunId}/stream",
            Children = response.Children.Select(AttachStreamUrl).ToArray()
        };
    }

    private async Task<string?> ResolveToolCallbackSecretAsync(string invocationId, CancellationToken cancellationToken)
    {
        if (_toolInvocationRepository is null || _toolDefinitionRepository is null)
        {
            return null;
        }

        var invocation = await _toolInvocationRepository.GetByInvocationIdAsync(invocationId, cancellationToken);
        if (invocation is null || string.IsNullOrWhiteSpace(invocation.ToolName))
        {
            return null;
        }

        var toolDefinition = await _toolDefinitionRepository.GetByToolNameAsync(invocation.ToolName, cancellationToken);
        return toolDefinition?.CallbackSecret;
    }

    private sealed record ApprovalActor(string? ApproverId, string? ApproverName, string? ApproverRole);

    private sealed record RunAccessContext(string? TenantId, string? UserId, string? SessionId);
}
