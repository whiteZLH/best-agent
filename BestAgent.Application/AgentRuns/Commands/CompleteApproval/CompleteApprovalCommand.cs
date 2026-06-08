using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CompleteApproval;

public record CompleteApprovalCommand(
    string RunId,
    string ApprovalId,
    string Decision,
    string? ApproverId,
    string? ApproverName,
    string? ApproverRole,
    string? Comment,
    string? IdempotencyKey = null) : IRequest<CompleteApprovalResult>;
