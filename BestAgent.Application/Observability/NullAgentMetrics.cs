namespace BestAgent.Application.Observability;

public sealed class NullAgentMetrics : IAgentMetrics
{
    public static NullAgentMetrics Instance { get; } = new();

    private NullAgentMetrics()
    {
    }

    public void RecordRunCreated(string agentCode, bool isChildRun)
    {
    }

    public void RecordRunCompleted(string agentCode, string status, decimal totalCost)
    {
    }

    public void RecordToolExecution(string toolName, string status, TimeSpan duration)
    {
    }

    public void RecordRetrieval(
        string status,
        bool queryRewritten,
        int sourceCount,
        int candidateCount,
        int selectedCount,
        TimeSpan duration)
    {
    }

    public void RecordModelCall(
        string model,
        string status,
        TimeSpan duration,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        decimal cost)
    {
    }

    public void RecordApprovalWaitStarted(string agentCode, string stepType)
    {
    }

    public void RecordApprovalWaitCompleted(string agentCode, string stepType, string outcome, TimeSpan duration)
    {
    }

    public void RecordApprovalTimedOut(string agentCode, string stepType, TimeSpan duration)
    {
    }

    public void RecordRunStreamOpened(bool replayRequested)
    {
    }

    public void RecordRunStreamEvent(string eventType, bool replay)
    {
    }

    public void RecordRunStreamCompleted(string outcome, int deliveredCount, TimeSpan duration)
    {
    }
}
