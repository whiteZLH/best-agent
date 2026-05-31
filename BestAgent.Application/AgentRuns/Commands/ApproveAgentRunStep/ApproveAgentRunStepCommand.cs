using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;

public record ApproveAgentRunStepCommand(
    string RunId,
    string StepId,
    string? ApproverId,
    string? ApproverName,
    string? ApproverRole,
    string? Comment) : IRequest<ApproveAgentRunStepResult>;
