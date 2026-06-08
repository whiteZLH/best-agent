using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CompleteToolInvocation;

public record CompleteToolInvocationCommand(
    string RunId,
    string InvocationId,
    string WaitToken,
    string ToolResult,
    string? IdempotencyKey = null) : IRequest<CompleteToolInvocationResult>;
