using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Queries.GetAgentRunEvents;

public class GetAgentRunEventsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnRunEventsInRepositoryOrder()
    {
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(outboxRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);

        outboxRepository.ListByRunIdAsync("run-1", 1, Arg.Any<CancellationToken>())
            .Returns(
            [
                new RunOutboxEvent
                {
                    EventId = "event-1",
                    RunId = "run-1",
                    SeqNo = 1,
                    EventType = "waiting",
                    RunStatus = "WaitingTool",
                    Payload = "{\"stepNo\":4}",
                    PublishStatus = "pending",
                    OccurredAt = now,
                    CreateTime = now,
                    LastModifyTime = now
                },
                new RunOutboxEvent
                {
                    EventId = "event-2",
                    RunId = "run-1",
                    SeqNo = 2,
                    EventType = "done",
                    RunStatus = "Completed",
                    Payload = "{\"stepNo\":0,\"stepType\":\"completed\",\"status\":\"Completed\",\"output\":\"{\\\"token\\\":\\\"secret-1\\\",\\\"value\\\":\\\"done\\\"}\",\"error\":null}",
                    PublishStatus = "published",
                    PublishedAt = now.AddSeconds(2),
                    RetryCount = 1,
                    OccurredAt = now.AddSeconds(1),
                    CreateTime = now.AddSeconds(1),
                    LastModifyTime = now.AddSeconds(2)
                }
            ]);

        var result = await mediator.Send(new GetAgentRunEventsQuery("run-1", 1));

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal("event-1", first.EventId);
                Assert.Equal(1, first.SeqNo);
                Assert.Equal("waiting", first.EventType);
                Assert.Equal("WaitingTool", first.RunStatus);
                Assert.NotNull(first.Data);
                Assert.Equal(4, first.Data!.StepNo);
                Assert.Equal("pending", first.PublishStatus);
            },
            second =>
            {
                Assert.Equal("event-2", second.EventId);
                Assert.Equal(2, second.SeqNo);
                Assert.Equal("done", second.EventType);
                Assert.Equal("Completed", second.RunStatus);
                Assert.Equal("{\"stepNo\":0,\"stepType\":\"completed\",\"status\":\"Completed\",\"output\":\"{\\u0022token\\u0022:\\u0022***\\u0022,\\u0022value\\u0022:\\u0022done\\u0022}\",\"error\":null}", second.Payload);
                Assert.NotNull(second.Data);
                Assert.Equal("completed", second.Data!.StepType);
                Assert.Equal("Completed", second.Data.Status);
                Assert.Equal("{\"token\":\"***\",\"value\":\"done\"}", second.Data.Output);
                Assert.Equal("published", second.PublishStatus);
                Assert.Equal(1, second.RetryCount);
            });
    }

    [Fact]
    public async Task Handle_ShouldReturnStructuredFailureInfo_WhenEventPayloadContainsToolError()
    {
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(outboxRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);

        outboxRepository.ListByRunIdAsync("run-1", null, Arg.Any<CancellationToken>())
            .Returns(
            [
                new RunOutboxEvent
                {
                    EventId = "event-3",
                    RunId = "run-1",
                    SeqNo = 3,
                    EventType = "error",
                    RunStatus = "Failed",
                    Payload = "{\"stepNo\":5,\"stepType\":\"tool_call\",\"status\":\"Failed\",\"output\":null,\"error\":\"{\\\"type\\\":\\\"tool_error\\\",\\\"toolName\\\":\\\"weather\\\",\\\"stage\\\":\\\"execution\\\",\\\"message\\\":\\\"backend crashed\\\",\\\"compensation\\\":{\\\"mode\\\":\\\"manual\\\"}}\"}",
                    PublishStatus = "pending",
                    OccurredAt = now,
                    CreateTime = now,
                    LastModifyTime = now
                }
            ]);

        var result = await mediator.Send(new GetAgentRunEventsQuery("run-1"));

        var evt = Assert.Single(result);
        var data = Assert.IsType<EventDataInfo>(evt.Data);
        var toolFailure = Assert.IsType<EventToolFailureInfo>(data.ToolFailure);
        Assert.Equal("weather", toolFailure.ToolName);
        Assert.Equal("execution", toolFailure.Stage);
        Assert.Equal("backend crashed", toolFailure.Message);
        Assert.Equal("manual", Assert.IsType<EventToolFailureCompensationInfo>(toolFailure.Compensation).Mode);
    }
}
