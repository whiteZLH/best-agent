using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.RequestHumanAgentRun;

public record RequestHumanAgentRunCommand(
    string RunId,
    string? Comment,
    string? SourceStepId,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole) : IRequest<RequestHumanAgentRunResult>;
