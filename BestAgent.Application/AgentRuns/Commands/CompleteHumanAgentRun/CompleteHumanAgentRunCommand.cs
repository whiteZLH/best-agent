using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CompleteHumanAgentRun;

public record CompleteHumanAgentRunCommand(
    string RunId,
    string StepId,
    string WaitToken,
    string? HumanResult,
    string? Comment,
    bool Terminate,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole) : IRequest<CompleteHumanAgentRunResult>;
