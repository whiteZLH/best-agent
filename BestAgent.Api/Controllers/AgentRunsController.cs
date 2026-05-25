using System;
using Microsoft.AspNetCore.Mvc;

namespace BestAgent.Api.Controllers;

[ApiController]
[Route("agent-runs")]
public class AgentRunsController : ControllerBase
{
    [HttpPost]
    public ActionResult<CreateAgentRunResponse> Create([FromBody] CreateAgentRunRequest request)
    {
        var runId = Guid.NewGuid();

        var response = new CreateAgentRunResponse(
            runId,
            request.AgentCode,
            request.Input,
            "Created");

        return CreatedAtAction(nameof(GetById), new { runId }, response);
    }

}
