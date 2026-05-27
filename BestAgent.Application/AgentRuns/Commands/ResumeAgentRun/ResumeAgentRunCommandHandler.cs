using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;

public class ResumeAgentRunCommandHandler : IRequestHandler<ResumeAgentRunCommand, ResumeAgentRunResult>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentRunChannel _agentRunChannel;

    public ResumeAgentRunCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentRunChannel agentRunChannel)
    {
        _agentRunRepository = agentRunRepository;
        _agentRunChannel = agentRunChannel;
    }

    public async Task<ResumeAgentRunResult> Handle(ResumeAgentRunCommand request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
            throw new NotFoundException("AgentRun", request.RunId);

        if (agentRun.Status != "WaitingTool")
            throw new ConflictException($"Run '{request.RunId}' is in status '{agentRun.Status}', expected 'WaitingTool'.");

        if (agentRun.CurrentWaitToken != request.WaitToken)
            throw new ConflictException($"Wait token mismatch for run '{request.RunId}'.");

        agentRun = agentRun with
        {
            Status = "Running",
            CurrentWaitToken = string.Empty,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = DateTime.UtcNow
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

        await _agentRunChannel.EnqueueAsync(
            new ResumeAgentRunMessage(request.RunId, request.WaitToken, request.ToolResult),
            cancellationToken);

        return new ResumeAgentRunResult(agentRun.RunId, agentRun.AgentCode, agentRun.InputPayload, null, "Running");
    }
}
