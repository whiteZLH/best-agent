using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CancelAgentRun;

public record CancelAgentRunCommand(
    string RunId,
    string? Reason) : IRequest<CancelAgentRunResult>;
