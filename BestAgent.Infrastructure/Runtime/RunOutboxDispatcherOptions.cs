namespace BestAgent.Infrastructure.Runtime;

public sealed class RunOutboxDispatcherOptions
{
    public int BatchSize { get; init; } = 100;

    public int PollIntervalSeconds { get; init; } = 2;

    public int MaxRetryCount { get; init; } = 3;
}
