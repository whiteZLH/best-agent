using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Queries.GetAgentRunById;

public class GetAgentRunByIdQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnCurrentToolWaitPointers_WhenRunIsWaitingTool()
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
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingTool",
            InputPayload = "hello",
            CurrentStepNo = 4,
            CurrentWaitToken = "wait-1",
            CreateTime = now,
            LastModifyTime = now
        };
        var currentStep = new AgentStep
        {
            StepId = "step-4",
            RunId = run.RunId,
            StepNo = 4,
            StepType = "tool_call",
            Status = "Pending",
            CreateTime = now,
            LastModifyTime = now
        };
        var invocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = currentStep.StepId,
            ToolName = "weather",
            Status = "Pending",
            CallbackToken = "wait-1",
            CreateTime = now,
            LastModifyTime = now
        };

        runRepository.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        stepRepository.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(currentStep);
        toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(run.RunId, currentStep.StepId, Arg.Any<CancellationToken>())
            .Returns(invocation);
        approvalRepository.GetByRunIdAndStepIdAsync(run.RunId, currentStep.StepId, Arg.Any<CancellationToken>())
            .Returns((AgentApproval?)null);

        var result = await mediator.Send(new GetAgentRunByIdQuery(run.RunId));

        Assert.NotNull(result);
        Assert.Equal("step-4", result!.CurrentStepId);
        Assert.Equal("tool_call", result.WaitStepType);
        Assert.Equal("invocation-1", result.CurrentInvocationId);
        Assert.Null(result.CurrentApprovalId);
        Assert.NotNull(result.CurrentToolInvocation);
        Assert.Equal("weather", result.CurrentToolInvocation!.ToolName);
        Assert.Equal("Pending", result.CurrentToolInvocation.Status);
        Assert.Null(result.CurrentApproval);
        Assert.Null(result.CurrentHumanWait);
        Assert.Null(result.CurrentHandoff);
    }

    [Fact]
    public async Task Handle_ShouldReturnCurrentApprovalPointers_WhenRunIsWaitingApproval()
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
        var run = new AgentRun
        {
            RunId = "run-2",
            AgentCode = "writer",
            Status = "WaitingApproval",
            InputPayload = "hello",
            CurrentStepNo = 5,
            CurrentWaitToken = "approval-wait-1",
            CreateTime = now,
            LastModifyTime = now
        };
        var currentStep = new AgentStep
        {
            StepId = "step-5",
            RunId = run.RunId,
            StepNo = 5,
            StepType = "approval_request",
            Status = "Pending",
            CreateTime = now,
            LastModifyTime = now
        };
        var approval = new AgentApproval
        {
            ApprovalId = "approval-1",
            RunId = run.RunId,
            StepId = currentStep.StepId,
            RequestedAction = "issue_refund",
            RiskLevel = "external_write",
            Decision = "Pending",
            WaitToken = "approval-wait-1",
            CreateTime = now,
            LastModifyTime = now
        };

        runRepository.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        stepRepository.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(currentStep);
        toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(run.RunId, currentStep.StepId, Arg.Any<CancellationToken>())
            .Returns((ToolInvocation?)null);
        approvalRepository.GetByRunIdAndStepIdAsync(run.RunId, currentStep.StepId, Arg.Any<CancellationToken>())
            .Returns(approval);

        var result = await mediator.Send(new GetAgentRunByIdQuery(run.RunId));

        Assert.NotNull(result);
        Assert.Equal("step-5", result!.CurrentStepId);
        Assert.Equal("approval_request", result.WaitStepType);
        Assert.Null(result.CurrentInvocationId);
        Assert.Equal("approval-1", result.CurrentApprovalId);
        Assert.NotNull(result.CurrentApproval);
        Assert.Equal("issue_refund", result.CurrentApproval!.ToolName);
        Assert.Equal("approval-1", result.CurrentApproval.ApprovalId);
        Assert.Null(result.CurrentToolInvocation);
        Assert.Null(result.CurrentHumanWait);
        Assert.Null(result.CurrentHandoff);
    }

    [Fact]
    public async Task Handle_ShouldReturnCurrentHumanAndHandoffContexts_WhenRunIsWaitingHumanOrHandoff()
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
        var humanRun = new AgentRun
        {
            RunId = "run-3",
            AgentCode = "writer",
            Status = "WaitingHuman",
            InputPayload = "hello",
            CurrentStepNo = 6,
            CurrentWaitToken = "human-wait-1",
            CreateTime = now,
            LastModifyTime = now
        };
        var humanStep = new AgentStep
        {
            StepId = "step-6",
            RunId = humanRun.RunId,
            StepNo = 6,
            StepType = "human_wait",
            Status = "Pending",
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(
                HumanApprovalPayloadSerializer.CreatePending(
                    "Need operator input",
                    sourceType: "tool_wait",
                    sourceStepId: "step-4",
                    sourceInvocationId: "invocation-1",
                    sourceToolName: "weather",
                    sourceToolInput: "{\"password\":\"secret-1\"}",
                    sourceToolOutput: "{\"authorization\":\"secret-2\"}",
                    sourceToolStatus: "Pending",
                    continueAsToolResult: true)),
            CreateTime = now,
            LastModifyTime = now
        };
        var handoffRun = new AgentRun
        {
            RunId = "run-4",
            AgentCode = "writer",
            Status = "WaitingHandoff",
            InputPayload = "hello",
            CurrentStepNo = 7,
            CurrentWaitToken = "handoff-wait-1",
            CreateTime = now,
            LastModifyTime = now
        };
        var handoffStep = new AgentStep
        {
            StepId = "step-7",
            RunId = handoffRun.RunId,
            StepNo = 7,
            StepType = "handoff",
            Status = "Pending",
            DecisionPayload = HandoffPayloadSerializer.Serialize(
                HandoffPayloadSerializer.CreatePending(
                    "handoff-wait-1",
                    "support_agent",
                    "Handle refund request",
                    "delegate_and_merge",
                    "child-run-1",
                    routeRuleId: "route-rule-1",
                    contextScope: "{\"mode\":\"summary_only\"}",
                    memoryScope: "{\"mode\":\"read_only\"}",
                    toolScope: "{\"allowed\":[\"faq_search\"]}",
                    knowledgeScope: "{\"allowed\":[\"faq\"]}",
                    approvalRequired: false,
                    reason: "Route to refund specialist",
                    confidence: 0.91,
                    contextOverrides: "{\"mode\":\"summary_only\"}",
                    memoryOverrides: "{\"mode\":\"read_only\"}",
                    toolOverrides: "{\"allowed\":[\"faq_search\"]}",
                    knowledgeOverrides: "{\"allowed\":[\"faq\"]}",
                    mergeStrategy: "first_success")),
            CreateTime = now,
            LastModifyTime = now
        };

        runRepository.GetByRunIdAsync("run-3", Arg.Any<CancellationToken>()).Returns(humanRun);
        runRepository.GetByRunIdAsync("run-4", Arg.Any<CancellationToken>()).Returns(handoffRun);
        stepRepository.GetLastByRunIdAsync("run-3", Arg.Any<CancellationToken>()).Returns(humanStep);
        stepRepository.GetLastByRunIdAsync("run-4", Arg.Any<CancellationToken>()).Returns(handoffStep);
        toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ToolInvocation?)null);
        approvalRepository.GetByRunIdAndStepIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AgentApproval?)null);

        var humanResult = await mediator.Send(new GetAgentRunByIdQuery("run-3"));
        var handoffResult = await mediator.Send(new GetAgentRunByIdQuery("run-4"));

        Assert.NotNull(humanResult);
        Assert.NotNull(humanResult!.CurrentHumanWait);
        Assert.Equal("tool_wait", humanResult.CurrentHumanWait!.SourceType);
        Assert.Equal("{\"password\":\"***\"}", humanResult.CurrentHumanWait.SourceToolInput);
        Assert.Equal("{\"authorization\":\"***\"}", humanResult.CurrentHumanWait.SourceToolOutput);
        Assert.True(humanResult.CurrentHumanWait.ContinueAsToolResult);
        Assert.Null(humanResult.CurrentHandoff);

        Assert.NotNull(handoffResult);
        Assert.NotNull(handoffResult!.CurrentHandoff);
        Assert.Equal("support_agent", handoffResult.CurrentHandoff!.TargetAgent);
        Assert.Equal("delegate_and_merge", handoffResult.CurrentHandoff.Mode);
        Assert.Equal("first_success", handoffResult.CurrentHandoff.MergeStrategy);
        Assert.Null(handoffResult.CurrentHumanWait);
    }
}
