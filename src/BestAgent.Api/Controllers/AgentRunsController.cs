using BestAgent.Application.AgentRuns;
using BestAgent.Application.AgentRuns.Commands;
using BestAgent.Application.AgentRuns.Queries;
using BestAgent.Contracts.AgentRuns;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BestAgent.Api.Controllers;

[ApiController]
[Route("agent-runs")]
public sealed class AgentRunsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AgentRunsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AgentRunResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentRunResponse>> CreateAsync(
        [FromBody] CreateAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateAgentRunCommand(
            request.AgentCode,
            request.SessionId,
            request.UserId,
            request.IdempotencyKey,
            request.Input.Text);

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(MapRun(result));
    }

    [HttpGet("{runId}")]
    [ProducesResponseType(typeof(AgentRunResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentRunResponse>> GetByIdAsync(string runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAgentRunByIdQuery(runId), cancellationToken);
        return Ok(MapRun(result));
    }

    [HttpGet("{runId}/steps")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentStepResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentStepResponse>>> GetStepsAsync(string runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAgentRunStepsQuery(runId), cancellationToken);
        return Ok(result.Select(MapStep).ToList());
    }

    [HttpPost("{runId}:resume")]
    [ProducesResponseType(typeof(AgentRunResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentRunResponse>> ResumeAsync(
        string runId,
        [FromBody] ResumeAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        var result = await _mediator.Send(new ResumeAgentRunCommand(runId), cancellationToken);
        return Ok(MapRun(result));
    }

    private static AgentRunResponse MapRun(AgentRunModel model)
    {
        return new AgentRunResponse
        {
            RunId = model.RunId,
            AgentCode = model.AgentCode,
            Status = model.Status,
            Output = model.Output,
            ErrorMessage = model.ErrorMessage,
            CurrentStepNo = model.CurrentStepNo,
            IdempotencyKey = model.IdempotencyKey
        };
    }

    private static AgentStepResponse MapStep(AgentStepModel model)
    {
        return new AgentStepResponse
        {
            StepId = model.StepId,
            StepNo = model.StepNo,
            StepType = model.StepType,
            Status = model.Status,
            InputPayload = model.InputPayload,
            OutputPayload = model.OutputPayload,
            ErrorPayload = model.ErrorPayload
        };
    }
}
