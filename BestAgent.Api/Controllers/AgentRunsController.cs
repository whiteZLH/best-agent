using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Runtime;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BestAgent.Api.Controllers;

[ApiController]
[Route("agent-runs")]
public class AgentRunsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly IAgentRunEventBus _eventBus;

    public AgentRunsController(IMediator mediator, IMapper mapper, IAgentRunEventBus eventBus)
    {
        _mediator = mediator;
        _mapper = mapper;
        _eventBus = eventBus;
    }

    [HttpPost]
    public async Task<ActionResult<CreateAgentRunResponse>> Create(
        [FromBody] CreateAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<CreateAgentRunCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        var response = _mapper.Map<CreateAgentRunResponse>(result);

        return Created($"/agent-runs/{response.RunId}", response);
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<GetAgentRunResponse>> GetById(
        [FromRoute] string runId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAgentRunByIdQuery(runId), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<GetAgentRunResponse>(result));
    }

    [HttpGet("{runId}/steps")]
    public async Task<ActionResult<IReadOnlyList<GetAgentRunStepResponse>>> GetSteps(
        [FromRoute] string runId,
        CancellationToken cancellationToken)
    {
        var steps = await _mediator.Send(new GetAgentRunStepsQuery(runId), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetAgentRunStepResponse>>(steps));
    }

    [HttpGet("{runId}/approvals")]
    public async Task<ActionResult<IReadOnlyList<GetAgentRunApprovalResponse>>> GetApprovals(
        [FromRoute] string runId,
        CancellationToken cancellationToken)
    {
        var approvals = await _mediator.Send(new GetAgentRunApprovalsQuery(runId), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetAgentRunApprovalResponse>>(approvals));
    }

    [HttpPost("{runId}:resume")]
    public async Task<ActionResult<ResumeAgentRunResponse>> Resume(
        [FromRoute] string runId,
        [FromBody] ResumeAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ResumeAgentRunCommand(runId, request.WaitToken, request.ToolResult);
        var result = await _mediator.Send(command, cancellationToken);
        var response = _mapper.Map<ResumeAgentRunResponse>(result);

        return Ok(response);
    }

    [HttpPost("{runId}/steps/{stepId}:approve")]
    public async Task<ActionResult<ApproveAgentRunStepResponse>> Approve(
        [FromRoute] string runId,
        [FromRoute] string stepId,
        [FromBody] ApproveAgentRunStepRequest request,
        CancellationToken cancellationToken)
    {
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
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        await foreach (var evt in _eventBus.SubscribeAsync(runId, cancellationToken))
        {
            var data = JsonSerializer.Serialize(evt.Data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await Response.WriteAsync($"event: {evt.EventType}\ndata: {data}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
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

    private sealed record ApprovalActor(string? ApproverId, string? ApproverName, string? ApproverRole);
}
