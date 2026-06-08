using System.Collections.Concurrent;
using System.Diagnostics;

namespace BestAgent.Api.Tests.Observability;

public sealed class ActivityTestCollector : IDisposable
{
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ActivityListener _listener;

    public ActivityTestCollector(string sourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, sourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Enqueue(activity)
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Activities => _activities.ToArray();

    public void Dispose()
    {
        _listener.Dispose();
    }
}
