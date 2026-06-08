using BestAgent.Application.AgentRuns.Runtime;
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
        var repository = Substitute.For<IRunOutboxEventRepository>();
        var publisher = Substitute.For<IRunOutboxEventPublisher>();
        var outboxEvent = CreateOutboxEvent("event-1");

        repository.ListPendingAsync(100, Arg.Any<CancellationToken>())
            .Returns([outboxEvent]);
        using var services = CreateServiceProvider(repository, publisher);
        var dispatcher = new RunOutboxEventDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RunOutboxEventDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(1, dispatched);
        await publisher.Received(1).PublishAsync(outboxEvent, Arg.Any<CancellationToken>());
        await repository.Received(1).MarkPublishedAsync(
            "event-1",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkFailedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_ShouldMarkFailed_WhenPublisherThrows()
    {
        var repository = Substitute.For<IRunOutboxEventRepository>();
        var publisher = Substitute.For<IRunOutboxEventPublisher>();
        var outboxEvent = CreateOutboxEvent("event-1");

        repository.ListPendingAsync(100, Arg.Any<CancellationToken>())
            .Returns([outboxEvent]);
        publisher.PublishAsync(outboxEvent, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("publish failed"));
        using var services = CreateServiceProvider(repository, publisher);
        var dispatcher = new RunOutboxEventDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RunOutboxEventDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(0, dispatched);
        await repository.Received(1).MarkFailedAsync("event-1", Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkPublishedAsync(
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
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
