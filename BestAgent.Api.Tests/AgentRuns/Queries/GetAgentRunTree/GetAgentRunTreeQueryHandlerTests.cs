using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunTree;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Queries.GetAgentRunTree;

public class GetAgentRunTreeQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnRecursiveRunTree()
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
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);

        var rootRun = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "Completed",
            InputPayload = "root input",
            OutputPayload = "root output",
            CurrentStepNo = 6,
            RootRunId = "run-1",
            CreateTime = now,
            LastModifyTime = now,
            StartedAt = now,
            EndedAt = now
        };
        var childRun = new AgentRun
        {
            RunId = "child-run-1",
            AgentCode = "support_agent",
            Status = "Completed",
            InputPayload = "child input",
            OutputPayload = "child output",
            CurrentStepNo = 4,
            ParentRunId = "run-1",
            RootRunId = "run-1",
            DelegatedByRunId = "run-1",
            DelegatedByAgent = "writer",
            CreateTime = now.AddMinutes(1),
            LastModifyTime = now.AddMinutes(1)
        };
        var grandChildRun = new AgentRun
        {
            RunId = "grandchild-run-1",
            AgentCode = "finance_agent",
            Status = "Failed",
            InputPayload = "grandchild input",
            CurrentStepNo = 2,
            ParentRunId = "child-run-1",
            RootRunId = "run-1",
            DelegatedByRunId = "child-run-1",
            DelegatedByAgent = "support_agent",
            InterruptReason = "backend unavailable",
            CurrentWaitToken = "wait-grandchild",
            CreateTime = now.AddMinutes(2),
            LastModifyTime = now.AddMinutes(2)
        };
        var grandChildStep = new AgentStep
        {
            StepId = "step-grandchild-1",
            RunId = grandChildRun.RunId,
            StepNo = 2,
            StepType = "handoff",
            Status = "Pending",
            DecisionPayload = HandoffPayloadSerializer.Serialize(
                HandoffPayloadSerializer.CreatePending(
                    "wait-grandchild",
                    "finance_agent",
                    "review transaction",
                    "delegate_and_merge",
                    "child-run-2",
                    routeRuleId: "route-rule-1",
                    contextScope: "{\"mode\":\"summary_only\"}",
                    memoryScope: "{\"mode\":\"read_only\"}",
                    toolScope: "{\"allowed\":[\"faq_search\"]}",
                    knowledgeScope: "{\"allowed\":[\"faq\"]}",
                    approvalRequired: false,
                    reason: "Route to finance specialist",
                    confidence: 0.92,
                    contextOverrides: "{\"mode\":\"summary_only\"}",
                    memoryOverrides: "{\"mode\":\"read_only\"}",
                    toolOverrides: "{\"allowed\":[\"faq_search\"]}",
                    knowledgeOverrides: "{\"allowed\":[\"faq\"]}",
                    mergeStrategy: "first_success")),
            CreateTime = now.AddMinutes(2),
            LastModifyTime = now.AddMinutes(2)
        };

        runRepository.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(rootRun);
        runRepository.ListByParentRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([childRun]);
        runRepository.ListByParentRunIdAsync("child-run-1", Arg.Any<CancellationToken>())
            .Returns([grandChildRun]);
        runRepository.ListByParentRunIdAsync("grandchild-run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentRun>());
        stepRepository.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns((AgentStep?)null);
        stepRepository.GetLastByRunIdAsync("child-run-1", Arg.Any<CancellationToken>())
            .Returns((AgentStep?)null);
        stepRepository.GetLastByRunIdAsync("grandchild-run-1", Arg.Any<CancellationToken>())
            .Returns(grandChildStep);
        approvalRepository.GetByRunIdAndStepIdAsync("grandchild-run-1", grandChildStep.StepId, Arg.Any<CancellationToken>())
            .Returns((AgentApproval?)null);
        toolInvocationRepository.GetPendingByRunIdAndStepIdAsync("grandchild-run-1", grandChildStep.StepId, Arg.Any<CancellationToken>())
            .Returns((ToolInvocation?)null);

        var result = await mediator.Send(new GetAgentRunTreeQuery("run-1"));

        Assert.NotNull(result);
        Assert.Equal("run-1", result!.RunId);
        var child = Assert.Single(result.Children);
        Assert.Equal("child-run-1", child.RunId);
        Assert.Equal("run-1", child.ParentRunId);
        Assert.Equal("writer", child.DelegatedByAgent);
        var grandChild = Assert.Single(child.Children);
        Assert.Equal("grandchild-run-1", grandChild.RunId);
        Assert.Equal("child-run-1", grandChild.ParentRunId);
        Assert.Equal("support_agent", grandChild.DelegatedByAgent);
        Assert.Equal("backend unavailable", grandChild.InterruptReason);
        Assert.Equal("wait-grandchild", grandChild.WaitToken);
        Assert.Equal("step-grandchild-1", grandChild.CurrentStepId);
        Assert.Equal("handoff", grandChild.WaitStepType);
        Assert.NotNull(grandChild.CurrentHandoff);
        Assert.Equal("finance_agent", grandChild.CurrentHandoff!.TargetAgent);
        Assert.Equal("first_success", grandChild.CurrentHandoff.MergeStrategy);
        Assert.Empty(grandChild.Children);
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenRootRunDoesNotExist()
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

        runRepository.GetByRunIdAsync("missing-run", Arg.Any<CancellationToken>())
            .Returns((AgentRun?)null);

        var result = await mediator.Send(new GetAgentRunTreeQuery("missing-run"));

        Assert.Null(result);
    }
}
