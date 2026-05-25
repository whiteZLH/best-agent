using MediatR;

namespace BestAgent.Application.AgentRuns.Commands;

public sealed record CreateAgentRunCommand(
    string AgentCode,
    string SessionId,
    string UserId,
    string IdempotencyKey,
    string InputText) : IRequest<AgentRunModel>;
