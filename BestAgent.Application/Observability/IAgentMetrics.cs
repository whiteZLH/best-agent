namespace BestAgent.Application.Observability;

public interface IAgentMetrics
{
    void RecordRunCreated(string agentCode, bool isChildRun);

    void RecordRunCompleted(string agentCode, string status, decimal totalCost);

    void RecordToolExecution(string toolName, string status, TimeSpan duration);

    void RecordRetrieval(
        string status,
        bool queryRewritten,
        int sourceCount,
        int candidateCount,
        int selectedCount,
        TimeSpan duration);

    void RecordModelCall(
        string model,
        string status,
        TimeSpan duration,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        decimal cost);

    void RecordApprovalWaitStarted(string agentCode, string stepType);

    void RecordApprovalWaitCompleted(string agentCode, string stepType, string outcome, TimeSpan duration);

    void RecordApprovalTimedOut(string agentCode, string stepType, TimeSpan duration);

    void RecordRunStreamOpened(bool replayRequested);

    void RecordRunStreamEvent(string eventType, bool replay);

    void RecordRunStreamCompleted(string outcome, int deliveredCount, TimeSpan duration);
}
