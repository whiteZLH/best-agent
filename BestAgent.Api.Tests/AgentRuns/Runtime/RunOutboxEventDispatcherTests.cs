using BestAgent.Api.Tests.Observability;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Runtime;

public class RunOutboxEventDispatcherTests
{
    [Fact]
    public async Task DispatchPendingAsync_ShouldPublishPendingEvents_AndMarkPublished()
    {
        using var activities = new ActivityTestCollector(AgentTracing.SourceName);
        var repository = Substitute.For<IRunOutboxEventRepository>();
        var publisher = Substitute.For<IRunOutboxEventPublisher>();
        var agentMetrics = Substitute.For<IAgentMetrics>();
        var outboxEvent = CreateOutboxEvent("event-1");

        repository.ListPendingAsync(100, Arg.Any<CancellationToken>())
            .Returns([outboxEvent]);
        using var services = CreateServiceProvider(repository, publisher);
        var dispatcher = new RunOutboxEventDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RunOutboxEventDispatcher>.Instance,
            agentMetrics: agentMetrics);

        var dispatched = await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(1, dispatched);
        await publisher.Received(1).PublishAsync(outboxEvent, Arg.Any<CancellationToken>());
        await repository.Received(1).MarkPublishedAsync(
            "event-1",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkRetryPendingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkDeadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        agentMetrics.Received(1).RecordOutboxDispatch("done", "published", false);
        var activity = Assert.Single(
            activities.Activities,
            value => string.Equals(value.OperationName, AgentTracing.OutboxDispatchActivityName, StringComparison.Ordinal));
        Assert.Equal("event-1", activity.GetTagItem("bestagent.event_id"));
        Assert.Equal("published", activity.GetTagItem("bestagent.status"));
    }

    [Fact]
    public async Task DispatchPendingAsync_ShouldKeepEventPendingForRetry_WhenPublisherThrowsBeforeRetryLimit()
    {
        using var activities = new ActivityTestCollector(AgentTracing.SourceName);
        var repository = Substitute.For<IRunOutboxEventRepository>();
        var publisher = Substitute.For<IRunOutboxEventPublisher>();
        var agentMetrics = Substitute.For<IAgentMetrics>();
        var outboxEvent = CreateOutboxEvent("event-1");

        repository.ListPendingAsync(100, Arg.Any<CancellationToken>())
            .Returns([outboxEvent]);
        publisher.PublishAsync(outboxEvent, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("publish failed"));
        using var services = CreateServiceProvider(repository, publisher);
        var dispatcher = new RunOutboxEventDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RunOutboxEventDispatcher>.Instance,
            agentMetrics: agentMetrics);

        var dispatched = await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(0, dispatched);
        await repository.Received(1).MarkRetryPendingAsync("event-1", Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkDeadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkPublishedAsync(
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        agentMetrics.Received(1).RecordOutboxDispatch("done", "retry_pending", false);
        var activity = Assert.Single(
            activities.Activities,
            value => string.Equals(value.OperationName, AgentTracing.OutboxDispatchActivityName, StringComparison.Ordinal));
        Assert.Equal("retry_pending", activity.GetTagItem("bestagent.status"));
    }

    [Fact]
    public async Task DispatchPendingAsync_ShouldMarkDead_WhenPublisherThrowsAtRetryLimit()
    {
        using var activities = new ActivityTestCollector(AgentTracing.SourceName);
        var repository = Substitute.For<IRunOutboxEventRepository>();
        var publisher = Substitute.For<IRunOutboxEventPublisher>();
        var agentMetrics = Substitute.For<IAgentMetrics>();
        var outboxEvent = CreateOutboxEvent("event-1") with
        {
            RetryCount = 2
        };

        repository.ListPendingAsync(100, Arg.Any<CancellationToken>())
            .Returns([outboxEvent]);
        publisher.PublishAsync(outboxEvent, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("publish failed"));
        using var services = CreateServiceProvider(repository, publisher);
        var dispatcher = new RunOutboxEventDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RunOutboxEventDispatcher>.Instance,
            new RunOutboxDispatcherOptions
            {
                MaxRetryCount = 3
            },
            agentMetrics);

        var dispatched = await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(0, dispatched);
        await repository.Received(1).MarkDeadAsync("event-1", Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkRetryPendingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkPublishedAsync(
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        agentMetrics.Received(1).RecordOutboxDispatch("done", "dead", true);
        var activity = Assert.Single(
            activities.Activities,
            value => string.Equals(value.OperationName, AgentTracing.OutboxDispatchActivityName, StringComparison.Ordinal));
        Assert.Equal("dead", activity.GetTagItem("bestagent.status"));
    }

    private static ServiceProvider CreateServiceProvider(
        IRunOutboxEventRepository repository,
        IRunOutboxEventPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(publisher);
        return services.BuildServiceProvider();
    }

    private static RunOutboxEvent CreateOutboxEvent(string eventId)
    {
        var now = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
        return new RunOutboxEvent
        {
            EventId = eventId,
            RunId = "run-1",
            SeqNo = 1,
            EventType = "done",
            RunStatus = "Completed",
            Payload = "{\"stepNo\":0,\"stepType\":\"completed\",\"status\":\"Completed\",\"output\":\"done\"}",
            PublishStatus = "pending",
            OccurredAt = now,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }
}
