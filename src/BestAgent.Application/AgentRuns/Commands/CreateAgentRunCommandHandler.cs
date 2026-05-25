using BestAgent.Application.AgentRuns.Services;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands;

public sealed class CreateAgentRunCommandHandler : IRequestHandler<CreateAgentRunCommand, AgentRunModel>
{
    private readonly AgentRuntimeService _agentRuntimeService;

    public CreateAgentRunCommandHandler(AgentRuntimeService agentRuntimeService)
    {
        _agentRuntimeService = agentRuntimeService;
    }

    public Task<AgentRunModel> Handle(CreateAgentRunCommand request, CancellationToken cancellationToken)
    {
        var runtimeRequest = new RuntimeRequest(
            request.AgentCode,
            request.SessionId,
            request.UserId,
            request.IdempotencyKey,
            request.InputText);

        return _agentRuntimeService.CreateAsync(runtimeRequest, cancellationToken);
    }
}
