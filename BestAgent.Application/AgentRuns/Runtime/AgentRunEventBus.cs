using System.Threading.Channels;

namespace BestAgent.Application.AgentRuns.Runtime;

public interface IAgentRunEventBus
{
    void Publish(AgentRunEvent evt);
    IAsyncEnumerable<AgentRunEvent> SubscribeAsync(string runId, CancellationToken cancellationToken);
}

public class AgentRunEventBus : IAgentRunEventBus
{
    private readonly ConcurrentSubscriptions _subscriptions = new();

    public void Publish(AgentRunEvent evt)
    {
        _subscriptions.Write(evt.RunId, evt);
    }

    public IAsyncEnumerable<AgentRunEvent> SubscribeAsync(string runId, CancellationToken cancellationToken)
    {
        return _subscriptions.Subscribe(runId, cancellationToken);
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

                if (evt.EventType is "done" or "error")
                {
                    foreach (var ch in subscribers)
                        ch.Writer.TryComplete();
                }
            }
        }

        public async IAsyncEnumerable<AgentRunEvent> Subscribe(
            string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
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

            try
            {
                await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
                    yield return evt;
            }
            finally
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
        }
    }
}
