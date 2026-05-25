using BestAgent.Application.Abstractions;

namespace BestAgent.Application.AgentRuns.Commands;

public sealed class ResumeAgentRunCommandValidator : IRequestValidator<ResumeAgentRunCommand>
{
    public IEnumerable<string> Validate(ResumeAgentRunCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            yield return "runId is required.";
        }
    }
}
