using AutoMapper;
using BestAgent.Api.Contracts.AgentDefinitions;
using BestAgent.Application.AgentDefinitions.Commands.ActivateAgentDefinitionVersion;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinition;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinitionVersion;
using BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitionByCode;
using BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitions;
using BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitionVersions;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BestAgent.Api.Controllers;

[ApiController]
[Route("agent-definitions")]
public class AgentDefinitionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public AgentDefinitionsController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GetAgentDefinitionResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var definitions = await _mediator.Send(new GetAgentDefinitionsQuery(), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetAgentDefinitionResponse>>(definitions));
    }

    [HttpGet("{agentCode}")]
    public async Task<ActionResult<GetAgentDefinitionResponse>> GetByCode(
        [FromRoute] string agentCode,
        CancellationToken cancellationToken)
    {
        var definition = await _mediator.Send(new GetAgentDefinitionByCodeQuery(agentCode), cancellationToken);
        if (definition is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<GetAgentDefinitionResponse>(definition));
    }

    [HttpPost]
    public async Task<ActionResult<GetAgentDefinitionResponse>> Create(
        [FromBody] CreateAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<CreateAgentDefinitionCommand>(request);
        var definition = await _mediator.Send(command, cancellationToken);
        var response = _mapper.Map<GetAgentDefinitionResponse>(definition);

        return Created($"/agent-definitions/{response.Code}", response);
    }

    [HttpGet("{agentCode}/versions")]
    public async Task<ActionResult<IReadOnlyList<GetAgentDefinitionVersionResponse>>> GetVersions(
        [FromRoute] string agentCode,
        CancellationToken cancellationToken)
    {
        var definition = await _mediator.Send(new GetAgentDefinitionByCodeQuery(agentCode), cancellationToken);
        if (definition is null)
        {
            return NotFound();
        }

        var versions = await _mediator.Send(new GetAgentDefinitionVersionsQuery(agentCode), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetAgentDefinitionVersionResponse>>(versions));
    }

    [HttpPost("{agentCode}/versions")]
    public async Task<ActionResult<GetAgentDefinitionVersionResponse>> CreateVersion(
        [FromRoute] string agentCode,
        [FromBody] CreateAgentDefinitionVersionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<CreateAgentDefinitionVersionCommand>(request) with { AgentCode = agentCode };
        var version = await _mediator.Send(command, cancellationToken);
        var response = _mapper.Map<GetAgentDefinitionVersionResponse>(version);

        return Created($"/agent-definitions/{agentCode}/versions/{response.Version}", response);
    }

    [HttpPost("{agentCode}:activate-version")]
    public async Task<ActionResult<GetAgentDefinitionResponse>> ActivateVersion(
        [FromRoute] string agentCode,
        [FromBody] ActivateAgentDefinitionVersionRequest request,
        CancellationToken cancellationToken)
    {
        var definition = await _mediator.Send(
            new ActivateAgentDefinitionVersionCommand(agentCode, request.Version),
            cancellationToken);

        return Ok(_mapper.Map<GetAgentDefinitionResponse>(definition));
    }
}
