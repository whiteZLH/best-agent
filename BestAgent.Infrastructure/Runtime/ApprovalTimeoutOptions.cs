namespace BestAgent.Infrastructure.Runtime;

public sealed class ApprovalTimeoutOptions
{
    public const string RejectAction = "reject";
    public const string RequestHumanAction = "request_human";

    public int TimeoutMinutes { get; init; } = 30;

    public int PollIntervalSeconds { get; init; } = 5;

    public int BatchSize { get; init; } = 100;

    public string TimeoutComment { get; init; } = "Approval timed out.";

    public string TimeoutAction { get; init; } = RejectAction;
}
