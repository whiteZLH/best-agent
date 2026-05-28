using System.Text.Json;
using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
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
        var result = await _mediator.Send(new ApproveAgentRunStepCommand(runId, stepId), cancellationToken);
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
        var result = await _mediator.Send(new RejectAgentRunStepCommand(runId, stepId, request.Comment), cancellationToken);
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
}
