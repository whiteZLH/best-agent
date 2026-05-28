using System.Threading.Channels;

namespace BestAgent.Application.AgentRuns.Runtime;

public abstract record AgentRunMessage(string RunId);

public record CreateAgentRunMessage(string RunId) : AgentRunMessage(RunId);

public record ResumeAgentRunMessage(
    string RunId,
    string WaitToken,
    string ToolResult) : AgentRunMessage(RunId);

public record ApproveAgentRunStepMessage(
    string RunId,
    string StepId) : AgentRunMessage(RunId);

public record RejectAgentRunStepMessage(
    string RunId,
    string StepId,
    string? Comment) : AgentRunMessage(RunId);

public interface IAgentRunChannel
{
    ValueTask EnqueueAsync(AgentRunMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentRunMessage> ReadAllAsync(CancellationToken cancellationToken);
}

public class AgentRunChannel : IAgentRunChannel
{
    private readonly Channel<AgentRunMessage> _channel = Channel.CreateUnbounded<AgentRunMessage>();

    public ValueTask EnqueueAsync(AgentRunMessage message, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(message, cancellationToken);
    }

    public IAsyncEnumerable<AgentRunMessage> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
