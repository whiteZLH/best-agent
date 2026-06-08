using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

public record CreateAgentRunCommand(
    string AgentCode,
    string Input,
    string? IdempotencyKey = null,
    string? TenantId = null,
    string? UserId = null,
    string? SessionId = null,
    bool? Stream = null,
    int? MaxRounds = null) : IRequest<CreateAgentRunResult>;
