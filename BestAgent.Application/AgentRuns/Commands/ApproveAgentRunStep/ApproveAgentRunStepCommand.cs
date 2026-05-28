using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;

public record ApproveAgentRunStepCommand(
    string RunId,
    string StepId) : IRequest<ApproveAgentRunStepResult>;
