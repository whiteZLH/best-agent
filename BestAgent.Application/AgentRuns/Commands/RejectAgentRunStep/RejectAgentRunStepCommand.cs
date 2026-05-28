using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;

public record RejectAgentRunStepCommand(
    string RunId,
    string StepId,
    string? Comment) : IRequest<RejectAgentRunStepResult>;
