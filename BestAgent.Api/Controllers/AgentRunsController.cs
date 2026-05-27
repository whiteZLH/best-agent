using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BestAgent.Api.Controllers;

[ApiController]
[Route("agent-runs")]
public class AgentRunsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public AgentRunsController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
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
}
