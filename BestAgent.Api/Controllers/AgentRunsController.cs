using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
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
}
