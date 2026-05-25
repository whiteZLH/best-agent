using BestAgent.Application.AgentRuns.Services;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands;

public sealed class ResumeAgentRunCommandHandler : IRequestHandler<ResumeAgentRunCommand, AgentRunModel>
{
    private readonly AgentRuntimeService _agentRuntimeService;

    public ResumeAgentRunCommandHandler(AgentRuntimeService agentRuntimeService)
    {
        _agentRuntimeService = agentRuntimeService;
    }

    public Task<AgentRunModel> Handle(ResumeAgentRunCommand request, CancellationToken cancellationToken)
    {
        return _agentRuntimeService.ResumeAsync(request.RunId, cancellationToken);
    }
}
