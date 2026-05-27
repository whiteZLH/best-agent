using AutoMapper;
using BestAgent.Api.Contracts.Tools;
using BestAgent.Application.Tools;
using BestAgent.Application.Tools.Commands.CreateToolDefinition;
using BestAgent.Application.Tools.Commands.UpdateToolDefinition;
using BestAgent.Application.Tools.Queries.GetToolDefinitionByName;
using BestAgent.Application.Tools.Queries.GetToolDefinitions;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BestAgent.Api.Controllers;

[ApiController]
[Route("tool-definitions")]
public class ToolDefinitionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public ToolDefinitionsController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GetToolDefinitionResponse>>> GetAll(
        [FromQuery] bool? enabledOnly,
        CancellationToken cancellationToken)
    {
        var tools = await _mediator.Send(new GetToolDefinitionsQuery(enabledOnly), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetToolDefinitionResponse>>(tools));
    }

    [HttpGet("{toolName}")]
    public async Task<ActionResult<GetToolDefinitionResponse>> GetByName(
        [FromRoute] string toolName,
        CancellationToken cancellationToken)
    {
        var tool = await _mediator.Send(new GetToolDefinitionByNameQuery(toolName), cancellationToken);
        if (tool is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<GetToolDefinitionResponse>(tool));
    }

    [HttpPost]
    public async Task<ActionResult<GetToolDefinitionResponse>> Create(
        [FromBody] CreateToolDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<CreateToolDefinitionCommand>(request);
        var tool = await _mediator.Send(command, cancellationToken);
        var response = _mapper.Map<GetToolDefinitionResponse>(tool);

        return Created($"/tool-definitions/{response.ToolName}", response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<GetToolDefinitionResponse>> Update(
        [FromRoute] string id,
        [FromBody] UpdateToolDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateToolDefinitionCommand(
            id,
            request.DisplayName,
            request.Description,
            request.InputSchema,
            request.OutputSchema,
            request.SideEffectLevel,
            request.TimeoutMs,
            request.RetryPolicy,
            request.AuthPolicy,
            request.IdempotencyPolicy,
            request.AsyncSupported,
            request.ConsistencyMode,
            request.CompensationPolicy,
            request.Enabled);

        var tool = await _mediator.Send(command, cancellationToken);
        return Ok(_mapper.Map<GetToolDefinitionResponse>(tool));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(new Application.Tools.Commands.DeleteToolDefinition.DeleteToolDefinitionCommand(id), cancellationToken);
        return NoContent();
    }
}
