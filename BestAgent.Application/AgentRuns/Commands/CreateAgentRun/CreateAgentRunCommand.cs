using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

public record CreateAgentRunCommand(
    string AgentCode,
    string Input) : IRequest<CreateAgentRunResult>;
