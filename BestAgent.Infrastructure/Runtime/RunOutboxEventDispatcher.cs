using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BestAgent.Infrastructure.Runtime;

public class RunOutboxEventDispatcher : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunOutboxEventDispatcher> _logger;

    public RunOutboxEventDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<RunOutboxEventDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRunOutboxEventRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRunOutboxEventPublisher>();
        var pendingEvents = await repository.ListPendingAsync(BatchSize, cancellationToken);
        var dispatched = 0;

        foreach (var outboxEvent in pendingEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await publisher.PublishAsync(outboxEvent, cancellationToken);
                await repository.MarkPublishedAsync(outboxEvent.EventId, DateTime.UtcNow, cancellationToken);
                dispatched++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to dispatch outbox event {EventId} for run {RunId}",
                    outboxEvent.EventId,
                    outboxEvent.RunId);
                await repository.MarkFailedAsync(outboxEvent.EventId, cancellationToken);
            }
        }

        return dispatched;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchPendingAsync(stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
