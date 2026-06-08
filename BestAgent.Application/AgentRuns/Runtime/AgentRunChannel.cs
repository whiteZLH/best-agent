using System.Threading.Channels;

namespace BestAgent.Application.AgentRuns.Runtime;

public abstract record AgentRunMessage(string RunId);

public record CreateAgentRunMessage(string RunId) : AgentRunMessage(RunId);

public record ResumeAgentRunMessage(
    string RunId,
    string WaitToken,
    string ToolResult,
    string? InvocationId = null) : AgentRunMessage(RunId);

public record ApproveAgentRunStepMessage(
    string RunId,
    string StepId,
    string? ApproverId,
    string? ApproverName,
    string? ApproverRole,
    string? Comment) : AgentRunMessage(RunId);

public record RejectAgentRunStepMessage(
    string RunId,
    string StepId,
    string? Comment,
    string? ApproverId,
    string? ApproverName,
    string? ApproverRole) : AgentRunMessage(RunId);

public record CompleteHumanAgentRunMessage(
    string RunId,
    string StepId,
    string WaitToken,
    string? HumanResult,
    string? Comment,
    bool Terminate,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole) : AgentRunMessage(RunId);

public record ResumeParentHandoffMessage(
    string RunId,
    string StepId,
    string WaitToken,
    string ChildRunId) : AgentRunMessage(RunId);

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
