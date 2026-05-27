using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;

public record ResumeAgentRunCommand(
    string RunId,
    string WaitToken,
    string ToolResult) : IRequest<ResumeAgentRunResult>;
