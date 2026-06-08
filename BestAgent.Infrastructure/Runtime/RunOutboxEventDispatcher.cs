using System.Diagnostics;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BestAgent.Infrastructure.Runtime;

public class RunOutboxEventDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunOutboxEventDispatcher> _logger;
    private readonly RunOutboxDispatcherOptions _options;
    private readonly IAgentMetrics _agentMetrics;

    public RunOutboxEventDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<RunOutboxEventDispatcher> logger,
        RunOutboxDispatcherOptions? options = null,
        IAgentMetrics? agentMetrics = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = NormalizeOptions(options);
        _agentMetrics = agentMetrics ?? NullAgentMetrics.Instance;
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRunOutboxEventRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRunOutboxEventPublisher>();
        var pendingEvents = await repository.ListPendingAsync(_options.BatchSize, cancellationToken);
        var dispatched = 0;

        foreach (var outboxEvent in pendingEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var activity = AgentTracing.Source.StartActivity(AgentTracing.OutboxDispatchActivityName, ActivityKind.Internal);
            activity?.SetTag("bestagent.event_id", outboxEvent.EventId);
            activity?.SetTag("bestagent.run_id", outboxEvent.RunId);
            activity?.SetTag("bestagent.seq_no", outboxEvent.SeqNo);
            activity?.SetTag("bestagent.event_type", outboxEvent.EventType);
            activity?.SetTag("bestagent.retry_count", outboxEvent.RetryCount);
            activity?.SetTag("bestagent.batch_size", _options.BatchSize);

            try
            {
                await publisher.PublishAsync(outboxEvent, cancellationToken);
                await repository.MarkPublishedAsync(outboxEvent.EventId, DateTime.UtcNow, cancellationToken);
                activity?.SetTag("bestagent.status", "published");
                _agentMetrics.RecordOutboxDispatch(outboxEvent.EventType, "published", outboxEvent.RetryCount > 0);
                dispatched++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to dispatch outbox event {EventId} for run {RunId}",
                    outboxEvent.EventId,
                    outboxEvent.RunId);
                if (outboxEvent.RetryCount + 1 >= _options.MaxRetryCount)
                {
                    await repository.MarkDeadAsync(outboxEvent.EventId, cancellationToken);
                    activity?.SetTag("bestagent.status", "dead");
                    _agentMetrics.RecordOutboxDispatch(outboxEvent.EventType, "dead", true);
                }
                else
                {
                    await repository.MarkRetryPendingAsync(outboxEvent.EventId, cancellationToken);
                    activity?.SetTag("bestagent.status", "retry_pending");
                    _agentMetrics.RecordOutboxDispatch(outboxEvent.EventType, "retry_pending", outboxEvent.RetryCount > 0);
                }
            }
        }

        return dispatched;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchPendingAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }
    }

    private static RunOutboxDispatcherOptions NormalizeOptions(RunOutboxDispatcherOptions? options)
    {
        return new RunOutboxDispatcherOptions
        {
            BatchSize = options?.BatchSize > 0 ? options.BatchSize : 100,
            PollIntervalSeconds = options?.PollIntervalSeconds > 0 ? options.PollIntervalSeconds : 2,
            MaxRetryCount = options?.MaxRetryCount > 0 ? options.MaxRetryCount : 3
        };
    }
}
