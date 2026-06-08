using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
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
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var toolInvocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(runRepository);
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(toolInvocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var earlier = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddMinutes(5);
        var childStep = new AgentStep
        {
            StepId = "step-child-2",
            RunId = "child-run-2",
            StepNo = 3,
            StepType = "human_wait",
            Status = "Pending",
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(
                HumanApprovalPayloadSerializer.CreatePending(
                    "Need operator input",
                    sourceType: "tool_wait",
                    sourceStepId: "step-1",
                    sourceInvocationId: "invocation-1",
                    sourceToolName: "weather",
                    sourceToolInput: "{\"password\":\"secret-1\"}",
                    sourceToolOutput: "{\"authorization\":\"secret-2\"}",
                    sourceToolStatus: "Pending",
                    continueAsToolResult: true)),
            CreateTime = later,
            LastModifyTime = later
        };

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
        stepRepository.GetLastByRunIdAsync("child-run-1", Arg.Any<CancellationToken>())
            .Returns((AgentStep?)null);
        stepRepository.GetLastByRunIdAsync("child-run-2", Arg.Any<CancellationToken>())
            .Returns(childStep);
        approvalRepository.GetByRunIdAndStepIdAsync("child-run-2", childStep.StepId, Arg.Any<CancellationToken>())
            .Returns((AgentApproval?)null);
        toolInvocationRepository.GetPendingByRunIdAndStepIdAsync("child-run-2", childStep.StepId, Arg.Any<CancellationToken>())
            .Returns((ToolInvocation?)null);

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
                Assert.Equal("step-child-2", second.CurrentStepId);
                Assert.Equal("human_wait", second.WaitStepType);
                Assert.NotNull(second.CurrentHumanWait);
                Assert.Equal("tool_wait", second.CurrentHumanWait!.SourceType);
                Assert.Equal("{\"password\":\"***\"}", second.CurrentHumanWait.SourceToolInput);
            });
    }
}
