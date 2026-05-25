using BestAgent.Application.Abstractions;

namespace BestAgent.Application.AgentRuns.Commands;

public sealed class CreateAgentRunCommandValidator : IRequestValidator<CreateAgentRunCommand>
{
    public IEnumerable<string> Validate(CreateAgentRunCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.AgentCode))
        {
            yield return "agentCode is required.";
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            yield return "idempotencyKey is required.";
        }

        if (string.IsNullOrWhiteSpace(request.InputText))
        {
            yield return "inputText is required.";
        }
    }
}
