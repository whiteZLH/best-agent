using System.Threading.Channels;

namespace BestAgent.Application.AgentRuns.Runtime;

public interface IAgentRunEventSubscription : IAsyncDisposable
{
    IAsyncEnumerable<AgentRunEvent> ReadAllAsync(CancellationToken cancellationToken);
}

public interface IAgentRunEventBus
{
    void Publish(AgentRunEvent evt);
    IAgentRunEventSubscription Subscribe(string runId);
    IAsyncEnumerable<AgentRunEvent> SubscribeAsync(string runId, CancellationToken cancellationToken);
}

public class AgentRunEventBus : IAgentRunEventBus
{
    private readonly ConcurrentSubscriptions _subscriptions = new();

    public void Publish(AgentRunEvent evt)
    {
        _subscriptions.Write(evt.RunId, evt);
    }

    public IAgentRunEventSubscription Subscribe(string runId)
    {
        return _subscriptions.Subscribe(runId);
    }

    public IAsyncEnumerable<AgentRunEvent> SubscribeAsync(string runId, CancellationToken cancellationToken)
    {
        return SubscribeCoreAsync(runId, cancellationToken);
    }

    private sealed class ConcurrentSubscriptions
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, List<Channel<AgentRunEvent>>> _channels = new();

        public void Write(string runId, AgentRunEvent evt)
        {
            List<Channel<AgentRunEvent>>? subscribers;
            lock (_lock)
            {
                _channels.TryGetValue(runId, out subscribers);
            }

            if (subscribers is null) return;

            lock (subscribers)
            {
                foreach (var ch in subscribers)
                    ch.Writer.TryWrite(evt);

                if (evt.EventType is "done" or "error" or "cancelled")
                {
                    foreach (var ch in subscribers)
                        ch.Writer.TryComplete();
                }
            }
        }

        public IAgentRunEventSubscription Subscribe(string runId)
        {
            var channel = Channel.CreateUnbounded<AgentRunEvent>();
            lock (_lock)
            {
                if (!_channels.TryGetValue(runId, out var list))
                {
                    list = new List<Channel<AgentRunEvent>>();
                    _channels[runId] = list;
                }

                lock (list) { list.Add(channel); }
            }

            return new Subscription(channel, () => Remove(runId, channel));
        }

        private void Remove(string runId, Channel<AgentRunEvent> channel)
        {
            lock (_lock)
            {
                if (_channels.TryGetValue(runId, out var list))
                {
                    lock (list)
                    {
                        list.Remove(channel);
                        if (list.Count == 0) _channels.Remove(runId);
                    }
                }
            }
        }

        private sealed class Subscription : IAgentRunEventSubscription
        {
            private readonly Channel<AgentRunEvent> _channel;
            private readonly Action _unsubscribe;
            private int _disposed;

            public Subscription(Channel<AgentRunEvent> channel, Action unsubscribe)
            {
                _channel = channel;
                _unsubscribe = unsubscribe;
            }

            public IAsyncEnumerable<AgentRunEvent> ReadAllAsync(CancellationToken cancellationToken)
            {
                return _channel.Reader.ReadAllAsync(cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return ValueTask.CompletedTask;
                }

                _unsubscribe();
                _channel.Writer.TryComplete();
                return ValueTask.CompletedTask;
            }
        }
    }

    private async IAsyncEnumerable<AgentRunEvent> SubscribeCoreAsync(
        string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var subscription = Subscribe(runId);
        await foreach (var evt in subscription.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }
}
