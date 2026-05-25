using BestAgent.Application.Planning;
using BestAgent.Domain.Agents;
using BestAgent.Domain.Messages;
using BestAgent.Domain.Runs;

namespace BestAgent.Application.AgentRuns.Services;

public sealed class ContextBuilder
{
    public ModelContext Build(AgentDefinition definition, AgentRun run, IReadOnlyList<AgentMessage> messages)
    {
        var orderedMessages = messages
            .OrderBy(message => message.CreatedAt)
            .Select(message => new ModelMessage(message.Role, message.Content))
            .ToList();

        return new ModelContext(
            definition.Code,
            definition.Instruction,
            orderedMessages,
            definition.DefaultModel);
    }
}
