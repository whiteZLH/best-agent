using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Queries.GetAgentRunSteps;

public class GetAgentRunStepsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldMaskSensitiveToolInputsInStepApprovalAndHumanWait()
    {
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var invocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(invocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc);
        var toolStep = AgentRunLoop.CreateStep(
            "run-1",
            1,
            "tool_call",
            "Completed",
            "{\"city\":\"Shanghai\",\"apiKey\":\"secret-1\",\"nested\":{\"accessToken\":\"token-1\"}}",
            "{\"forecast\":\"sunny\",\"credential\":\"cred-1\"}",
            null,
            now,
            now);
        var humanStep = AgentRunLoop.CreateStep(
            "run-1",
            2,
            "human_wait",
            "Pending",
            "Need operator review",
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(
                HumanApprovalPayloadSerializer.CreatePending(
                    "Need operator review",
                    sourceType: "tool_wait",
                    sourceStepId: toolStep.StepId,
                    sourceInvocationId: "invocation-1",
                    sourceToolName: "weather",
                    sourceToolInput: "{\"password\":\"p@ss\",\"profile\":{\"refreshToken\":\"refresh-1\"}}",
                    sourceToolOutput: "{\"forecast\":\"rainy\",\"authorization\":\"Bearer top-secret\"}",
                    sourceToolStatus: "Completed",
                    continueAsToolResult: true))
        };

        stepRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([toolStep, humanStep]);
        approvalRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new AgentApproval
                {
                    ApprovalId = "approval-1",
                    RunId = "run-1",
                    StepId = toolStep.StepId,
                    RequestedAction = "weather",
                    RiskLevel = "internal_write",
                    RequestPayload = "{\"credential\":\"cred-1\",\"options\":{\"secret\":\"secret-2\"}}",
                    Decision = ApprovalDecisions.Pending,
                    WaitToken = "wait-1",
                    CreateTime = now,
                    LastModifyTime = now
                }
            ]);
        invocationRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new ToolInvocation
                {
                    InvocationId = "invocation-1",
                    RunId = "run-1",
                    StepId = toolStep.StepId,
                    ToolName = "weather",
                    Mode = "async",
                    Status = "Pending",
                    CallbackToken = "wait-1",
                    CreateTime = now,
                    LastModifyTime = now
                }
            ]);

        var result = await mediator.Send(new GetAgentRunStepsQuery("run-1"));

        Assert.Collection(
            result,
            item => Assert.Equal(toolStep.StepId, item.StepId),
            item => Assert.Equal(humanStep.StepId, item.StepId));
        var toolResult = result[0];
        Assert.Equal("{\"city\":\"Shanghai\",\"apiKey\":\"***\",\"nested\":{\"accessToken\":\"***\"}}", toolResult.Input);
        Assert.Equal("{\"forecast\":\"sunny\",\"credential\":\"***\"}", toolResult.Output);
        Assert.Equal("weather", toolResult.Approval!.RequestedAction);
        Assert.Equal("{\"credential\":\"***\",\"options\":{\"secret\":\"***\"}}", toolResult.Approval.RequestPayload);
        Assert.Equal("{\"credential\":\"***\",\"options\":{\"secret\":\"***\"}}", toolResult.Approval!.ToolInput);
        Assert.Equal("invocation-1", toolResult.ToolInvocation!.InvocationId);

        var humanResult = result[1];
        Assert.Equal("Need operator review", humanResult.Input);
        Assert.Equal("{\"password\":\"***\",\"profile\":{\"refreshToken\":\"***\"}}", humanResult.HumanWait!.SourceToolInput);
        Assert.Equal("{\"forecast\":\"rainy\",\"authorization\":\"***\"}", humanResult.HumanWait.SourceToolOutput);
    }

    [Fact]
    public async Task Handle_ShouldReturnHandoffInfo_WhenStepDecisionPayloadContainsHandoff()
    {
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var invocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(invocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var handoffStep = AgentRunLoop.CreateStep(
            "run-1",
            3,
            "handoff",
            "Completed",
            "Please help with refund",
            "Child answer",
            null,
            now,
            now) with
        {
            DecisionPayload = HandoffPayloadSerializer.Serialize(
                HandoffPayloadSerializer.MarkCompleted(
                    HandoffPayloadSerializer.CreatePending(
                        "handoff-wait-1",
                        "support_agent",
                        "Please help with refund",
                        "delegate_and_merge",
                        "child-run-1",
                        "route-rule-1",
                        "{\"mode\":\"summary_only\"}",
                        "{\"mode\":\"read_only\"}",
                        "{\"inherit\":false}",
                        "{\"sources\":[\"faq\"]}",
                        true,
                        "Route to refund specialist",
                        0.91,
                        "{\"mode\":\"summary_only\"}",
                        "{\"mode\":\"read_only\"}",
                        "{\"allowed\":[\"faq_search\"]}",
                        "{\"allowed\":[\"faq\"]}",
                        "first_success"),
                    "Completed",
                    "Child answer"))
        };

        stepRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([handoffStep]);
        approvalRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentApproval>());
        invocationRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ToolInvocation>());

        var result = await mediator.Send(new GetAgentRunStepsQuery("run-1"));

        var item = Assert.Single(result);
        Assert.Equal("handoff", item.StepType);
        Assert.NotNull(item.Handoff);
        Assert.Equal("handoff", item.Handoff!.WaitType);
        Assert.Equal("support_agent", item.Handoff.TargetAgent);
        Assert.Equal("Please help with refund", item.Handoff.HandoffInput);
        Assert.Equal("delegate_and_merge", item.Handoff.Mode);
        Assert.Equal("child-run-1", item.Handoff.ChildRunId);
        Assert.Equal("Approved", item.Handoff.Decision);
        Assert.Equal("Completed", item.Handoff.ChildStatus);
        Assert.Equal("Child answer", item.Handoff.ChildOutput);
        Assert.Equal("route-rule-1", item.Handoff.RouteRuleId);
        Assert.Equal("{\"mode\":\"summary_only\"}", item.Handoff.ContextScope);
        Assert.Equal("{\"mode\":\"read_only\"}", item.Handoff.MemoryScope);
        Assert.Equal("{\"inherit\":false}", item.Handoff.ToolScope);
        Assert.Equal("{\"sources\":[\"faq\"]}", item.Handoff.KnowledgeScope);
        Assert.True(item.Handoff.ApprovalRequired);
        Assert.Equal("Route to refund specialist", item.Handoff.Reason);
        Assert.Equal(0.91, item.Handoff.Confidence);
        Assert.Equal("{\"mode\":\"summary_only\"}", item.Handoff.ContextOverrides);
        Assert.Equal("{\"mode\":\"read_only\"}", item.Handoff.MemoryOverrides);
        Assert.Equal("{\"allowed\":[\"faq_search\"]}", item.Handoff.ToolOverrides);
        Assert.Equal("{\"allowed\":[\"faq\"]}", item.Handoff.KnowledgeOverrides);
        Assert.Equal("first_success", item.Handoff.MergeStrategy);
    }

    [Fact]
    public async Task Handle_ShouldReturnModelCallInfo_WhenStepDecisionPayloadContainsModelUsage()
    {
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var invocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(invocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var modelStep = AgentRunLoop.CreateStep(
            "run-1",
            1,
            "model_call",
            "Completed",
            "hello",
            "{\"action\":\"respond\",\"response\":\"Hi\"}",
            null,
            now,
            now) with
        {
            DecisionPayload = ModelCallPayloadSerializer.Create(
                "gpt-4o-mini",
                new BestAgent.Application.Models.GenerateTextResult(
                    "{\"action\":\"respond\",\"response\":\"Hi\"}",
                    PromptTokens: 120,
                    CompletionTokens: 45,
                    TotalTokens: 165,
                    Cost: 0.0042m,
                    FinishReason: "stop",
                    ReasoningSummary: "Need refund policy confirmation.",
                    ToolCalls:
                    [
                        new BestAgent.Application.Models.GenerateTextToolCall(
                            "call_123",
                            "function",
                            "weather",
                            "{\"city\":\"Shanghai\"}")
                    ]),
                new RuntimeRetrievalAudit(
                    "refund manager approval",
                    true,
                    4,
                    1,
                    ["faq"],
                    ["faq/doc-1#1"],
                    ["score=3; source=faq/doc-1#1; chunk=1"]))
        };

        stepRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([modelStep]);
        approvalRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentApproval>());
        invocationRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ToolInvocation>());

        var result = await mediator.Send(new GetAgentRunStepsQuery("run-1"));

        var item = Assert.Single(result);
        Assert.Equal("model_call", item.StepType);
        Assert.NotNull(item.ModelCall);
        Assert.Equal("gpt-4o-mini", item.ModelCall!.Model);
        Assert.Equal(120, item.ModelCall.PromptTokens);
        Assert.Equal(45, item.ModelCall.CompletionTokens);
        Assert.Equal(165, item.ModelCall.TotalTokens);
        Assert.Equal(0.0042m, item.ModelCall.Cost);
        Assert.Equal("stop", item.ModelCall.FinishReason);
        Assert.Equal("Need refund policy confirmation.", item.ModelCall.ReasoningSummary);
        var toolCall = Assert.Single(item.ModelCall.ToolCalls!);
        Assert.Equal("call_123", toolCall.Id);
        Assert.Equal("function", toolCall.Type);
        Assert.Equal("weather", toolCall.Name);
        Assert.Equal("{\"city\":\"Shanghai\"}", toolCall.Arguments);
        Assert.NotNull(item.ModelCall.Retrieval);
        Assert.Equal("refund manager approval", item.ModelCall.Retrieval!.QueryText);
        Assert.True(item.ModelCall.Retrieval.WasRewritten);
        Assert.Equal(4, item.ModelCall.Retrieval.CandidateCount);
        Assert.Equal(1, item.ModelCall.Retrieval.SelectedCount);
        Assert.Equal("faq", Assert.Single(item.ModelCall.Retrieval.RequestedSources));
        Assert.Equal("faq/doc-1#1", Assert.Single(item.ModelCall.Retrieval.SelectedSources));
    }

    [Fact]
    public async Task Handle_ShouldReturnRetrievalInfo_WhenStepDecisionPayloadContainsExplicitRetrieval()
    {
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var invocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(invocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 9, 0, 0, 0, DateTimeKind.Utc);
        var retrievalStep = AgentRunLoop.CreateStep(
            "run-1",
            2,
            "retrieval",
            "Completed",
            "hotel refund manager approval policy",
            "hotel refund manager approval policy",
            null,
            now,
            now) with
        {
            DecisionPayload = RetrievalPayloadSerializer.Create("hotel refund manager approval policy")
        };

        stepRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([retrievalStep]);
        approvalRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentApproval>());
        invocationRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ToolInvocation>());

        var result = await mediator.Send(new GetAgentRunStepsQuery("run-1"));

        var item = Assert.Single(result);
        Assert.Equal("retrieval", item.StepType);
        Assert.NotNull(item.Retrieval);
        Assert.Equal("hotel refund manager approval policy", item.Retrieval!.QueryText);
    }

    [Fact]
    public async Task Handle_ShouldReturnStructuredFailureInfo_WhenStepErrorPayloadContainsModelAndToolFailures()
    {
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var invocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(invocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var modelFailureStep = AgentRunLoop.CreateStep(
            "run-1",
            1,
            "failed",
            "Failed",
            "hello",
            null,
            ModelFailurePayloadSerializer.Create("upstream_unavailable", "Planner could not continue."),
            now,
            now);
        var toolFailureStep = AgentRunLoop.CreateStep(
            "run-1",
            2,
            "tool_call",
            "Failed",
            "{\"city\":\"Shanghai\"}",
            null,
            ToolFailurePayloadSerializer.Create(
                "weather",
                "execution",
                "tool backend crashed",
                "{\"mode\":\"manual\"}"),
            now,
            now);

        stepRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([modelFailureStep, toolFailureStep]);
        approvalRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentApproval>());
        invocationRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ToolInvocation>());

        var result = await mediator.Send(new GetAgentRunStepsQuery("run-1"));

        Assert.Equal(2, result.Count);
        var modelFailure = Assert.IsType<ModelFailureInfo>(result[0].ModelFailure);
        Assert.Equal("upstream_unavailable", modelFailure.ErrorCode);
        Assert.Equal("Planner could not continue.", modelFailure.Message);
        Assert.Null(result[0].ToolFailure);

        var toolFailure = Assert.IsType<ToolFailureInfo>(result[1].ToolFailure);
        Assert.Equal("weather", toolFailure.ToolName);
        Assert.Equal("execution", toolFailure.Stage);
        Assert.Equal("tool backend crashed", toolFailure.Message);
        Assert.Equal("manual", Assert.IsType<ToolFailureCompensationInfo>(toolFailure.Compensation).Mode);
        Assert.Null(result[1].ModelFailure);
    }

    [Fact]
    public async Task Handle_ShouldReturnGenericApprovalFields_ForPlannerApprovalRequestStep()
    {
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var invocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(invocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var approvalStep = AgentRunLoop.CreateStep(
            "run-1",
            3,
            "approval_request",
            "Pending",
            "{\"amount\":120,\"currency\":\"USD\"}",
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(
                ApprovalPayloadSerializer.CreatePending(
                    "issue refund",
                    "{\"amount\":120,\"currency\":\"USD\"}",
                    "external_write",
                    "Refund exceeds auto-approval threshold."))
        };

        stepRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([approvalStep]);
        approvalRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentApproval>());
        invocationRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ToolInvocation>());

        var result = await mediator.Send(new GetAgentRunStepsQuery("run-1"));

        var item = Assert.Single(result);
        Assert.Equal("approval_request", item.StepType);
        Assert.NotNull(item.Approval);
        Assert.Equal("issue refund", item.Approval!.RequestedAction);
        Assert.Equal("{\"amount\":120,\"currency\":\"USD\"}", item.Approval.RequestPayload);
        Assert.Equal("issue refund", item.Approval.ToolName);
        Assert.Equal("{\"amount\":120,\"currency\":\"USD\"}", item.Approval.ToolInput);
        Assert.Equal("external_write", item.Approval.SideEffectLevel);
        Assert.Equal(ApprovalDecisions.Pending, item.Approval.Decision);
        Assert.Equal("Refund exceeds auto-approval threshold.", item.Approval.Comment);
    }
}
