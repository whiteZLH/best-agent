using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Queries.GetAgentRunChildren;

public class GetAgentRunChildrenQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnOrderedChildRuns_WithParentRelationshipFields()
    {
        var runRepository = Substitute.For<IAgentRunRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(runRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var earlier = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddMinutes(5);

        runRepository.ListByParentRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new AgentRun
                {
                    RunId = "child-run-1",
                    AgentCode = "support_agent",
                    Status = "Completed",
                    InputPayload = "help with refund",
                    OutputPayload = "refund approved",
                    ParentRunId = "run-1",
                    RootRunId = "root-run-1",
                    DelegatedByRunId = "run-1",
                    DelegatedByAgent = "writer",
                    CurrentStepNo = 4,
                    CreateTime = earlier,
                    LastModifyTime = earlier,
                    StartedAt = earlier,
                    EndedAt = earlier
                },
                new AgentRun
                {
                    RunId = "child-run-2",
                    AgentCode = "finance_agent",
                    Status = "Failed",
                    InputPayload = "check balance",
                    OutputPayload = null,
                    ParentRunId = "run-1",
                    RootRunId = "root-run-1",
                    DelegatedByRunId = "run-1",
                    DelegatedByAgent = "writer",
                    InterruptReason = "backend unavailable",
                    CurrentWaitToken = "wait-child-2",
                    CurrentStepNo = 3,
                    CreateTime = later,
                    LastModifyTime = later,
                    StartedAt = later
                }
            ]);

        var result = await mediator.Send(new GetAgentRunChildrenQuery("run-1"));

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal("child-run-1", first.RunId);
                Assert.Equal("support_agent", first.AgentCode);
                Assert.Equal("Completed", first.Status);
                Assert.Equal("help with refund", first.Input);
                Assert.Equal("refund approved", first.Output);
                Assert.Equal("run-1", first.ParentRunId);
                Assert.Equal("root-run-1", first.RootRunId);
                Assert.Equal("run-1", first.DelegatedByRunId);
                Assert.Equal("writer", first.DelegatedByAgent);
                Assert.Null(first.InterruptReason);
                Assert.Null(first.WaitToken);
            },
            second =>
            {
                Assert.Equal("child-run-2", second.RunId);
                Assert.Equal("finance_agent", second.AgentCode);
                Assert.Equal("Failed", second.Status);
                Assert.Equal("check balance", second.Input);
                Assert.Null(second.Output);
                Assert.Equal("backend unavailable", second.InterruptReason);
                Assert.Equal("wait-child-2", second.WaitToken);
            });
    }
}
