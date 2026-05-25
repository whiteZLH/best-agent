using MediatR;

namespace BestAgent.Application.AgentRuns.Commands;

public sealed record ResumeAgentRunCommand(string RunId) : IRequest<AgentRunModel>;
