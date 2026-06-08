using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Text.Json;
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
        var toolInvocationPayload = ToolInvocationEventPayloadSerializer.Create(
            "invocation-1",
            "weather",
            "async",
            "Pending",
            "wait-1");
        var escapedToolInvocationPayload = JsonSerializer.Serialize(toolInvocationPayload);

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
                    Payload = $$"""{"stepNo":4,"stepType":"tool_call","status":"Pending","toolInvocation":{{escapedToolInvocationPayload}}}""",
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
                Assert.NotNull(first.Data.ToolInvocation);
                Assert.Equal("invocation-1", first.Data.ToolInvocation!.InvocationId);
                Assert.Equal("weather", first.Data.ToolInvocation.ToolName);
                Assert.Equal("wait-1", first.Data.ToolInvocation.CallbackToken);
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

    [Fact]
    public async Task Handle_ShouldReturnStructuredModelCallInfo_WhenEventPayloadContainsModelCallAudit()
    {
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(outboxRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);
        var modelCallPayload = ModelCallPayloadSerializer.Create(
            "gpt-4o-mini",
            new BestAgent.Application.Models.GenerateTextResult(
                "{\"action\":\"respond\",\"response\":\"Hi\"}",
                PromptTokens: 120,
                CompletionTokens: 45,
                TotalTokens: 165,
                Cost: 0.0042m,
                FinishReason: "stop"),
            new RuntimeRetrievalAudit(
                "refund manager approval",
                true,
                4,
                1,
                ["faq"],
                ["faq/doc-1#1"],
                ["score=3; source=faq/doc-1#1; chunk=1"]));
        var escapedModelCallPayload = JsonSerializer.Serialize(modelCallPayload);

        outboxRepository.ListByRunIdAsync("run-1", null, Arg.Any<CancellationToken>())
            .Returns(
            [
                new RunOutboxEvent
                {
                    EventId = "event-4",
                    RunId = "run-1",
                    SeqNo = 4,
                    EventType = "step",
                    RunStatus = "Running",
                    Payload = $$"""{"stepNo":3,"stepType":"model_call","status":"Completed","output":"{\"action\":\"respond\",\"response\":\"Hi\"}","error":null,"modelCall":{{escapedModelCallPayload}}}""",
                    PublishStatus = "published",
                    OccurredAt = now,
                    CreateTime = now,
                    LastModifyTime = now
                }
            ]);

        var result = await mediator.Send(new GetAgentRunEventsQuery("run-1"));

        var evt = Assert.Single(result);
        var data = Assert.IsType<EventDataInfo>(evt.Data);
        Assert.NotNull(data.ModelCall);
        Assert.Equal("gpt-4o-mini", data.ModelCall!.Model);
        Assert.Equal(120, data.ModelCall.PromptTokens);
        Assert.Equal("stop", data.ModelCall.FinishReason);
        Assert.Equal("refund manager approval", data.ModelCall.Retrieval!.QueryText);
        Assert.True(data.ModelCall.Retrieval.WasRewritten);
        Assert.Equal("faq/doc-1#1", Assert.Single(data.ModelCall.Retrieval.SelectedSources));
    }

    [Fact]
    public async Task Handle_ShouldReturnStructuredRetrievalInfo_WhenEventPayloadContainsRetrievalDecisionPayload()
    {
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(outboxRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 9, 10, 0, 0, DateTimeKind.Utc);
        var retrievalPayload = RetrievalPayloadSerializer.Create("hotel refund manager approval policy");
        var escapedRetrievalPayload = JsonSerializer.Serialize(retrievalPayload);

        outboxRepository.ListByRunIdAsync("run-1", null, Arg.Any<CancellationToken>())
            .Returns(
            [
                new RunOutboxEvent
                {
                    EventId = "event-retrieval-1",
                    RunId = "run-1",
                    SeqNo = 8,
                    EventType = "step",
                    RunStatus = "Running",
                    Payload = $$"""{"stepNo":4,"stepType":"retrieval","status":"Completed","output":"hotel refund manager approval policy","error":null,"decisionPayload":{{escapedRetrievalPayload}}}""",
                    PublishStatus = "published",
                    OccurredAt = now,
                    CreateTime = now,
                    LastModifyTime = now
                }
            ]);

        var result = await mediator.Send(new GetAgentRunEventsQuery("run-1"));

        var evt = Assert.Single(result);
        var data = Assert.IsType<EventDataInfo>(evt.Data);
        Assert.NotNull(data.Retrieval);
        Assert.Equal("hotel refund manager approval policy", data.Retrieval!.QueryText);
    }

    [Fact]
    public async Task Handle_ShouldReturnStructuredDecisionInfo_WhenEventPayloadContainsDecisionPayload()
    {
        var outboxRepository = Substitute.For<IRunOutboxEventRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(outboxRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);
        var approvalDecisionPayload = ApprovalPayloadSerializer.Serialize(
            ApprovalPayloadSerializer.CreatePending(
                "issue_refund",
                "{\"token\":\"secret-1\"}",
                "external_write",
                "Need manager approval."));
        var handoffDecisionPayload = HandoffPayloadSerializer.Serialize(
            HandoffPayloadSerializer.CreatePending(
                "wait-1",
                "support_agent",
                "Handle refund request",
                "delegate_and_merge",
                "child-run-1",
                routeRuleId: "route-rule-1",
                contextScope: "{\"mode\":\"summary_only\"}",
                memoryScope: "{\"mode\":\"read_only\"}",
                toolScope: "{\"allowed\":[\"faq_search\"]}",
                knowledgeScope: "{\"allowed\":[\"faq\"]}",
                approvalRequired: true,
                reason: "Route to refund specialist",
                confidence: 0.91,
                contextOverrides: "{\"mode\":\"summary_only\"}",
                memoryOverrides: "{\"mode\":\"read_only\"}",
                toolOverrides: "{\"allowed\":[\"faq_search\"]}",
                knowledgeOverrides: "{\"allowed\":[\"faq\"]}",
                mergeStrategy: "first_success"));
        var humanDecisionPayload = HumanApprovalPayloadSerializer.Serialize(
            HumanApprovalPayloadSerializer.CreatePending(
                "Need operator input",
                sourceType: "tool_wait",
                sourceStepId: "step-0",
                sourceInvocationId: "invocation-0",
                sourceToolName: "weather",
                sourceToolInput: "{\"password\":\"secret-2\"}",
                sourceToolOutput: "{\"authorization\":\"secret-3\",\"forecast\":\"rainy\"}",
                sourceToolStatus: "Pending",
                continueAsToolResult: true));
        var escapedApprovalDecisionPayload = JsonSerializer.Serialize(approvalDecisionPayload);
        var escapedHandoffDecisionPayload = JsonSerializer.Serialize(handoffDecisionPayload);
        var escapedHumanDecisionPayload = JsonSerializer.Serialize(humanDecisionPayload);

        outboxRepository.ListByRunIdAsync("run-1", null, Arg.Any<CancellationToken>())
            .Returns(
            [
                new RunOutboxEvent
                {
                    EventId = "event-5",
                    RunId = "run-1",
                    SeqNo = 5,
                    EventType = "waiting_approval",
                    RunStatus = "WaitingApproval",
                    Payload = $$"""{"stepNo":4,"stepType":"approval_request","status":"Pending","output":"issue_refund","error":null,"decisionPayload":{{escapedApprovalDecisionPayload}}}""",
                    PublishStatus = "pending",
                    OccurredAt = now,
                    CreateTime = now,
                    LastModifyTime = now
                },
                new RunOutboxEvent
                {
                    EventId = "event-6",
                    RunId = "run-1",
                    SeqNo = 6,
                    EventType = "waiting_handoff",
                    RunStatus = "WaitingHandoff",
                    Payload = $$"""{"stepNo":5,"stepType":"handoff","status":"Pending","output":"support_agent","error":null,"decisionPayload":{{escapedHandoffDecisionPayload}}}""",
                    PublishStatus = "pending",
                    OccurredAt = now.AddSeconds(1),
                    CreateTime = now.AddSeconds(1),
                    LastModifyTime = now.AddSeconds(1)
                },
                new RunOutboxEvent
                {
                    EventId = "event-7",
                    RunId = "run-1",
                    SeqNo = 7,
                    EventType = "waiting_human",
                    RunStatus = "WaitingHuman",
                    Payload = $$"""{"stepNo":6,"stepType":"human_wait","status":"Pending","output":"Need operator input","error":null,"decisionPayload":{{escapedHumanDecisionPayload}}}""",
                    PublishStatus = "pending",
                    OccurredAt = now.AddSeconds(2),
                    CreateTime = now.AddSeconds(2),
                    LastModifyTime = now.AddSeconds(2)
                }
            ]);

        var result = await mediator.Send(new GetAgentRunEventsQuery("run-1"));

        Assert.Collection(
            result,
            approvalEvent =>
            {
                Assert.DoesNotContain("secret-1", approvalEvent.Payload);
                Assert.NotNull(approvalEvent.Data?.Approval);
                Assert.Equal("approval", approvalEvent.Data!.Approval!.WaitType);
                Assert.Equal("issue_refund", approvalEvent.Data.Approval.RequestedAction);
                Assert.Equal("{\"token\":\"***\"}", approvalEvent.Data.Approval.RequestPayload);
                Assert.Equal("Need manager approval.", approvalEvent.Data.Approval.Comment);
            },
            handoffEvent =>
            {
                Assert.NotNull(handoffEvent.Data?.Handoff);
                Assert.Equal("support_agent", handoffEvent.Data!.Handoff!.TargetAgent);
                Assert.Equal("delegate_and_merge", handoffEvent.Data.Handoff.Mode);
                Assert.Equal("child-run-1", handoffEvent.Data.Handoff.ChildRunId);
                Assert.True(handoffEvent.Data.Handoff.ApprovalRequired);
                Assert.Equal("first_success", handoffEvent.Data.Handoff.MergeStrategy);
            },
            humanEvent =>
            {
                Assert.DoesNotContain("secret-2", humanEvent.Payload);
                Assert.DoesNotContain("secret-3", humanEvent.Payload);
                Assert.NotNull(humanEvent.Data?.HumanWait);
                Assert.Equal("human", humanEvent.Data!.HumanWait!.WaitType);
                Assert.Equal("tool_wait", humanEvent.Data.HumanWait.SourceType);
                Assert.Equal("{\"password\":\"***\"}", humanEvent.Data.HumanWait.SourceToolInput);
                Assert.Equal("{\"authorization\":\"***\",\"forecast\":\"rainy\"}", humanEvent.Data.HumanWait.SourceToolOutput);
                Assert.True(humanEvent.Data.HumanWait.ContinueAsToolResult);
            });
    }
}
