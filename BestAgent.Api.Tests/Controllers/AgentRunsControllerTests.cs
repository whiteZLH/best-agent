using System.Security.Claims;
using System.Text;
using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Api.Controllers;
using BestAgent.Api.Infrastructure;
using BestAgent.Api.Mappings;
using BestAgent.Application.AgentRuns.Commands.CancelAgentRun;
using BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.CompleteHumanAgentRun;
using BestAgent.Application.AgentRuns.Commands.CompleteApproval;
using BestAgent.Application.AgentRuns.Commands.CompleteToolInvocation;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.RequestHumanAgentRun;
using BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunTree;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BestAgent.Api.Tests.Controllers;

public class AgentRunsControllerTests
{
    private readonly IMapper _mapper;

    public AgentRunsControllerTests()
    {
        var configuration = new MapperConfiguration(config =>
        {
            config.AddProfile<ApiMappingProfile>();
        }, NullLoggerFactory.Instance);

        _mapper = configuration.CreateMapper();
    }

    [Fact]
    public async Task Create()
    {
        var request = new CreateAgentRunRequest(
            "writer",
            "hello",
            "idem-1",
            "tenant-1",
            "user-1",
            "session-1",
            new CreateAgentRunOptionsRequest(true, 3));
        var mediator = new FakeMediator((CreateAgentRunCommand command) =>
        {
            Assert.Equal("writer", command.AgentCode);
            Assert.Equal("hello", command.Input);
            Assert.Equal("idem-1", command.IdempotencyKey);
            Assert.Equal("tenant-1", command.TenantId);
            Assert.Equal("user-1", command.UserId);
            Assert.Equal("session-1", command.SessionId);
            Assert.True(command.Stream);
            Assert.Equal(3, command.MaxRounds);

            return new CreateAgentRunResult("run-001", command.AgentCode, command.Input, "done", "Succeeded");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.Create(request, null, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal("/agent-runs/run-001", createdResult.Location);

        var response = Assert.IsType<CreateAgentRunResponse>(createdResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("writer", response.AgentCode);
        Assert.Equal("hello", response.Input);
        Assert.Equal("done", response.Output);
        Assert.Equal("Succeeded", response.Status);
    }

    [Fact]
    public async Task Create_ShouldPreferIdempotencyHeaderOverRequestBody()
    {
        var request = new CreateAgentRunRequest(
            "writer",
            "hello",
            "body-idem",
            "tenant-1",
            "user-1",
            "session-1",
            new CreateAgentRunOptionsRequest(false, 2));
        var mediator = new FakeMediator((CreateAgentRunCommand command) =>
        {
            Assert.Equal("header-idem", command.IdempotencyKey);
            Assert.Equal("tenant-1", command.TenantId);
            Assert.Equal("user-1", command.UserId);
            Assert.Equal("session-1", command.SessionId);
            Assert.False(command.Stream);
            Assert.Equal(2, command.MaxRounds);

            return new CreateAgentRunResult("run-001", command.AgentCode, command.Input, null, "Running");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.Create(request, " header-idem ", CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var response = Assert.IsType<CreateAgentRunResponse>(createdResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("Running", response.Status);
    }

    [Fact]
    public async Task Create_ShouldPreferAuthenticatedRunScopeOverRequestBody()
    {
        var request = new CreateAgentRunRequest(
            "writer",
            "hello",
            "body-idem",
            "body-tenant",
            "body-user",
            "body-session",
            new CreateAgentRunOptionsRequest(false, 2));
        var mediator = new FakeMediator((CreateAgentRunCommand command) =>
        {
            Assert.Equal("claim-tenant", command.TenantId);
            Assert.Equal("claim-user", command.UserId);
            Assert.Equal("claim-session", command.SessionId);

            return new CreateAgentRunResult("run-001", command.AgentCode, command.Input, null, "Running");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("claim-user", "Claim User", "reviewer", "claim-tenant", "claim-session")
                }
            }
        };

        var actionResult = await controller.Create(request, null, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var response = Assert.IsType<CreateAgentRunResponse>(createdResult.Value);
        Assert.Equal("run-001", response.RunId);
    }

    [Fact]
    public async Task Create_ShouldPreferScopedHeadersOverRequestBody_WhenUnauthenticated()
    {
        var request = new CreateAgentRunRequest(
            "writer",
            "hello",
            "body-idem",
            "body-tenant",
            "body-user",
            "body-session",
            new CreateAgentRunOptionsRequest(false, 2));
        var mediator = new FakeMediator((CreateAgentRunCommand command) =>
        {
            Assert.Equal("header-tenant", command.TenantId);
            Assert.Equal("header-user", command.UserId);
            Assert.Equal("header-session", command.SessionId);

            return new CreateAgentRunResult("run-001", command.AgentCode, command.Input, null, "Running");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["X-BestAgent-Tenant-Id"] = "header-tenant";
        controller.Request.Headers["X-BestAgent-User-Id"] = "header-user";
        controller.Request.Headers["X-BestAgent-Session-Id"] = "header-session";

        var actionResult = await controller.Create(request, null, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var response = Assert.IsType<CreateAgentRunResponse>(createdResult.Value);
        Assert.Equal("run-001", response.RunId);
    }

    [Fact]
    public async Task GetById()
    {
        var now = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunByIdQuery query) =>
            new GetAgentRunByIdResult(
                query.RunId,
                "writer",
                "Succeeded",
                "hello",
                "done",
                10,
                100,
                12.5m,
                now,
                now,
                now,
                now,
                4,
                "parent-1",
                "root-1",
                "delegator-run-1",
                "router",
                "Waiting for tool callback.",
                "wait-1"));
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetById("run-001", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetAgentRunResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("writer", response.AgentCode);
        Assert.Equal("Succeeded", response.Status);
        Assert.Equal("hello", response.Input);
        Assert.Equal("done", response.Output);
        Assert.Equal(10, response.MaxTurns);
        Assert.Equal(100, response.MaxCost);
        Assert.Equal(12.5m, response.TotalCost);
        Assert.Equal(4, response.CurrentStepNo);
        Assert.Equal("parent-1", response.ParentRunId);
        Assert.Equal("root-1", response.RootRunId);
        Assert.Equal("delegator-run-1", response.DelegatedByRunId);
        Assert.Equal("router", response.DelegatedByAgent);
        Assert.Equal("Waiting for tool callback.", response.InterruptReason);
        Assert.Equal("wait-1", response.WaitToken);
    }

    [Fact]
    public async Task GetById_ShouldRejectMismatchedAuthenticatedRunScope()
    {
        var mediator = new FakeMediator((GetAgentRunByIdQuery _) =>
            throw new InvalidOperationException("Mediator should not be invoked when access is forbidden."));
        var controller = new AgentRunsController(
            mediator,
            _mapper,
            new NullEventBus(),
            agentRunRepository: new FakeAgentRunRepository(
                new AgentRun
                {
                    RunId = "run-001",
                    AgentCode = "writer",
                    Status = "Running",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    InputPayload = "hello"
                }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("user-2", "Claim User", "reviewer", "tenant-2", "session-1")
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BestAgent.Application.Exceptions.ForbiddenException>(() =>
            controller.GetById("run-001", CancellationToken.None));

        Assert.Contains("outside the authenticated tenant or user scope", ex.Message);
    }

    [Fact]
    public async Task GetById_ShouldRejectMismatchedScopedHeaders_WhenUnauthenticated()
    {
        var mediator = new FakeMediator((GetAgentRunByIdQuery _) =>
            throw new InvalidOperationException("Mediator should not be invoked when access is forbidden."));
        var controller = new AgentRunsController(
            mediator,
            _mapper,
            new NullEventBus(),
            agentRunRepository: new FakeAgentRunRepository(
                new AgentRun
                {
                    RunId = "run-001",
                    AgentCode = "writer",
                    Status = "Running",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    InputPayload = "hello"
                }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["X-BestAgent-Tenant-Id"] = "tenant-2";

        var ex = await Assert.ThrowsAsync<BestAgent.Application.Exceptions.ForbiddenException>(() =>
            controller.GetById("run-001", CancellationToken.None));

        Assert.Contains("outside the authenticated tenant or user scope", ex.Message);
    }

    [Fact]
    public async Task CompleteToolInvocation_ShouldRejectMismatchedScopedHeaders_BeforeWebhookAuthorization()
    {
        var authorizer = new RecordingWebhookRequestAuthorizer();
        var controller = new AgentRunsController(
            new FakeMediator((CompleteToolInvocationCommand _) =>
                throw new InvalidOperationException("Mediator should not be invoked when access is forbidden.")),
            _mapper,
            new NullEventBus(),
            webhookRequestAuthorizer: authorizer,
            agentRunRepository: new FakeAgentRunRepository(
                new AgentRun
                {
                    RunId = "run-001",
                    AgentCode = "writer",
                    Status = "WaitingTool",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    InputPayload = "hello"
                }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["X-BestAgent-Tenant-Id"] = "tenant-2";

        var ex = await Assert.ThrowsAsync<BestAgent.Application.Exceptions.ForbiddenException>(() =>
            controller.CompleteToolInvocation(
                "run-001",
                "invocation-1",
                new CompleteToolInvocationRequest("wait-1", "{}"),
                null,
                CancellationToken.None));

        Assert.Contains("outside the authenticated tenant or user scope", ex.Message);
        Assert.Equal(0, authorizer.ToolAuthorizeCount);
    }

    [Fact]
    public async Task CompleteApproval_ShouldRejectMismatchedScopedHeaders_BeforeWebhookAuthorization()
    {
        var authorizer = new RecordingWebhookRequestAuthorizer();
        var controller = new AgentRunsController(
            new FakeMediator((CompleteApprovalCommand _) =>
                throw new InvalidOperationException("Mediator should not be invoked when access is forbidden.")),
            _mapper,
            new NullEventBus(),
            webhookRequestAuthorizer: authorizer,
            agentRunRepository: new FakeAgentRunRepository(
                new AgentRun
                {
                    RunId = "run-001",
                    AgentCode = "writer",
                    Status = "WaitingApproval",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    SessionId = "session-1",
                    InputPayload = "hello"
                }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["X-BestAgent-User-Id"] = "user-2";

        var ex = await Assert.ThrowsAsync<BestAgent.Application.Exceptions.ForbiddenException>(() =>
            controller.CompleteApproval(
                "run-001",
                "approval-1",
                new CompleteApprovalRequest("Approved", "u-1", "Alice", "admin", "ok"),
                null,
                CancellationToken.None));

        Assert.Contains("outside the authenticated tenant or user scope", ex.Message);
        Assert.Equal(0, authorizer.ApprovalAuthorizeCount);
    }

    [Fact]
    public async Task GetSteps()
    {
        var now = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunStepsQuery query) =>
            (IReadOnlyList<GetAgentRunStepsItem>)
            [
                new(
                    "step-001",
                    1,
                    "Plan",
                    "Succeeded",
                    $"input:{query.RunId}",
                    "output",
                    null,
                    "plan",
                    new HandoffInfo("handoff", "support_agent", "handoff-input", "delegate_and_merge", "child-run-1", "Approved", "Completed", "child-output", null, now, "route-rule-1", "{\"mode\":\"summary_only\"}", "{\"mode\":\"read_only\"}", "{\"inherit\":false}", "{\"sources\":[\"faq\"]}", true, "Route to refund specialist", 0.91, "{\"mode\":\"summary_only\"}", "{\"mode\":\"read_only\"}", "{\"allowed\":[\"faq_search\"]}", "{\"allowed\":[\"faq\"]}", "first_success"),
                    new ApprovalInfo("approval", "weather", "{}", "internal_write", "Pending", null, null, "approval-1", "u-1", "Alice", "admin"),
                    new HumanWaitInfo("human", "Pending", "Need operator input", null, "u-2", "Bob", "operator", null, "tool_wait", "step-0", "invocation-0", "weather", "{}", null, "Pending", true),
                    new ToolInvocationInfo("invocation-1", "weather", "async", "Pending", "wait-1", now, null, 0),
                    new ModelCallInfo("gpt-4o-mini", 120, 45, 165, 0.0042m),
                    new ModelFailureInfo("upstream_unavailable", "Planner could not continue."),
                    new ToolFailureInfo("weather", "execution", "tool backend crashed", new ToolFailureCompensationInfo("manual")),
                    now,
                    now,
                    now,
                    now,
                    1200)
            ]);
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetSteps("run-001", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetAgentRunStepResponse>>(okResult.Value);
        var step = Assert.Single(response);
        Assert.Equal("step-001", step.StepId);
        Assert.Equal(1, step.StepNo);
        Assert.Equal("Plan", step.StepType);
        Assert.Equal("Succeeded", step.Status);
        Assert.Equal("input:run-001", step.Input);
        Assert.Equal("output", step.Output);
        Assert.Equal("plan", step.StepKey);
        Assert.Equal("handoff", step.Handoff!.WaitType);
        Assert.Equal("support_agent", step.Handoff.TargetAgent);
        Assert.Equal("handoff-input", step.Handoff.HandoffInput);
        Assert.Equal("delegate_and_merge", step.Handoff.Mode);
        Assert.Equal("child-run-1", step.Handoff.ChildRunId);
        Assert.Equal("Approved", step.Handoff.Decision);
        Assert.Equal("Completed", step.Handoff.ChildStatus);
        Assert.Equal("child-output", step.Handoff.ChildOutput);
        Assert.Equal("route-rule-1", step.Handoff.RouteRuleId);
        Assert.Equal("{\"mode\":\"summary_only\"}", step.Handoff.ContextScope);
        Assert.Equal("{\"mode\":\"read_only\"}", step.Handoff.MemoryScope);
        Assert.Equal("{\"inherit\":false}", step.Handoff.ToolScope);
        Assert.Equal("{\"sources\":[\"faq\"]}", step.Handoff.KnowledgeScope);
        Assert.True(step.Handoff.ApprovalRequired);
        Assert.Equal("Route to refund specialist", step.Handoff.Reason);
        Assert.Equal(0.91, step.Handoff.Confidence);
        Assert.Equal("{\"mode\":\"summary_only\"}", step.Handoff.ContextOverrides);
        Assert.Equal("{\"mode\":\"read_only\"}", step.Handoff.MemoryOverrides);
        Assert.Equal("{\"allowed\":[\"faq_search\"]}", step.Handoff.ToolOverrides);
        Assert.Equal("{\"allowed\":[\"faq\"]}", step.Handoff.KnowledgeOverrides);
        Assert.Equal("first_success", step.Handoff.MergeStrategy);
        Assert.Equal("approval", step.Approval!.WaitType);
        Assert.Equal("weather", step.Approval.ToolName);
        Assert.Equal("Pending", step.Approval.Decision);
        Assert.Equal("approval-1", step.Approval.ApprovalId);
        Assert.Equal("Alice", step.Approval.ApproverName);
        Assert.Equal("human", step.HumanWait!.WaitType);
        Assert.Equal("Pending", step.HumanWait.Decision);
        Assert.Equal("Need operator input", step.HumanWait.Comment);
        Assert.Equal("Bob", step.HumanWait.HumanOperatorName);
        Assert.Equal("tool_wait", step.HumanWait.SourceType);
        Assert.Equal("step-0", step.HumanWait.SourceStepId);
        Assert.Equal("invocation-0", step.HumanWait.SourceInvocationId);
        Assert.Equal("weather", step.HumanWait.SourceToolName);
        Assert.Equal("{}", step.HumanWait.SourceToolInput);
        Assert.Equal("Pending", step.HumanWait.SourceToolStatus);
        Assert.True(step.HumanWait.ContinueAsToolResult);
        Assert.Equal("invocation-1", step.ToolInvocation!.InvocationId);
        Assert.Equal("weather", step.ToolInvocation.ToolName);
        Assert.Equal("async", step.ToolInvocation.Mode);
        Assert.Equal("Pending", step.ToolInvocation.Status);
        Assert.Equal("wait-1", step.ToolInvocation.CallbackToken);
        Assert.Equal("gpt-4o-mini", step.ModelCall!.Model);
        Assert.Equal(120, step.ModelCall.PromptTokens);
        Assert.Equal(45, step.ModelCall.CompletionTokens);
        Assert.Equal(165, step.ModelCall.TotalTokens);
        Assert.Equal(0.0042m, step.ModelCall.Cost);
        Assert.Equal("upstream_unavailable", step.ModelFailure!.ErrorCode);
        Assert.Equal("Planner could not continue.", step.ModelFailure.Message);
        Assert.Equal("weather", step.ToolFailure!.ToolName);
        Assert.Equal("execution", step.ToolFailure.Stage);
        Assert.Equal("tool backend crashed", step.ToolFailure.Message);
        Assert.Equal("manual", step.ToolFailure.Compensation!.Mode);
        Assert.Equal(1200, step.DurationMs);
    }

    [Fact]
    public async Task GetChildren()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunChildrenQuery query) =>
            (IReadOnlyList<GetAgentRunChildrenItem>)
            [
                new(
                    "child-run-1",
                    "support_agent",
                    "Completed",
                    $"input:{query.RunId}",
                    "child-output",
                    now,
                    now,
                    now,
                    now,
                    4,
                    query.RunId,
                    "root-run-1",
                    query.RunId,
                    "writer",
                    null,
                    null)
            ]);
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetChildren("run-001", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetAgentRunChildResponse>>(okResult.Value);
        var child = Assert.Single(response);
        Assert.Equal("child-run-1", child.RunId);
        Assert.Equal("support_agent", child.AgentCode);
        Assert.Equal("Completed", child.Status);
        Assert.Equal("input:run-001", child.Input);
        Assert.Equal("child-output", child.Output);
        Assert.Equal("run-001", child.ParentRunId);
        Assert.Equal("root-run-1", child.RootRunId);
        Assert.Equal("run-001", child.DelegatedByRunId);
        Assert.Equal("writer", child.DelegatedByAgent);
    }

    [Fact]
    public async Task GetTree()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunTreeQuery query) =>
            new GetAgentRunTreeItem(
                query.RunId,
                "writer",
                "Completed",
                "root input",
                "root output",
                now,
                now,
                now,
                now,
                6,
                null,
                query.RunId,
                null,
                null,
                null,
                null,
                [
                    new GetAgentRunTreeItem(
                        "child-run-1",
                        "support_agent",
                        "Completed",
                        "child input",
                        "child output",
                        now,
                        now,
                        now,
                        now,
                        4,
                        query.RunId,
                        query.RunId,
                        query.RunId,
                        "writer",
                        null,
                        null,
                        [])
                ]));
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetTree("run-001", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetAgentRunTreeResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        var child = Assert.Single(response.Children);
        Assert.Equal("child-run-1", child.RunId);
        Assert.Equal("run-001", child.ParentRunId);
        Assert.Equal("writer", child.DelegatedByAgent);
    }

    [Fact]
    public async Task GetTree_ShouldReturnNotFound_WhenRunDoesNotExist()
    {
        var mediator = new FakeMediator((GetAgentRunTreeQuery _) => (GetAgentRunTreeItem?)null);
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetTree("missing-run", CancellationToken.None);

        Assert.IsType<NotFoundResult>(actionResult.Result);
    }

    [Fact]
    public async Task GetApprovals()
    {
        var now = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunApprovalsQuery query) =>
            (IReadOnlyList<GetAgentRunApprovalsItem>)
            [
                new(
                    "approval-1",
                    query.RunId,
                    "step-1",
                    "weather",
                    "internal_write",
                    "{}",
                    "Approved",
                    "u-1",
                    "admin",
                    "Alice",
                    "Looks good",
                    "wait-1",
                    null,
                    now,
                    now,
                    now)
            ]);
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetApprovals("run-001", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetAgentRunApprovalResponse>>(okResult.Value);
        var approval = Assert.Single(response);
        Assert.Equal("approval-1", approval.ApprovalId);
        Assert.Equal("run-001", approval.RunId);
        Assert.Equal("Approved", approval.Decision);
        Assert.Equal("Alice", approval.ApproverName);
    }

    [Fact]
    public async Task GetEvents()
    {
        var now = new DateTime(2026, 6, 6, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunEventsQuery query) =>
            (IReadOnlyList<GetAgentRunEventsItem>)
            [
                new(
                    "event-1",
                    query.RunId,
                    1,
                    "done",
                    "Completed",
                    "{\"stepNo\":0,\"stepType\":\"completed\",\"status\":\"Completed\",\"output\":\"{\\\"token\\\":\\\"***\\\",\\\"value\\\":\\\"done\\\"}\",\"error\":null}",
                    new EventDataInfo(0, "completed", "Completed", "{\"token\":\"***\",\"value\":\"done\"}", null, null, null),
                    "pending",
                    null,
                    0,
                    now,
                    now,
                    now)
            ]);
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetEvents("run-001", 12, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetAgentRunEventResponse>>(okResult.Value);
        var evt = Assert.Single(response);
        Assert.Equal("event-1", evt.EventId);
        Assert.Equal("run-001", evt.RunId);
        Assert.Equal(12, mediator.LastGetEventsQuery?.AfterSeqNo);
        Assert.Equal(1, evt.SeqNo);
        Assert.Equal("done", evt.EventType);
        Assert.Equal("Completed", evt.RunStatus);
        Assert.Equal("{\"stepNo\":0,\"stepType\":\"completed\",\"status\":\"Completed\",\"output\":\"{\\\"token\\\":\\\"***\\\",\\\"value\\\":\\\"done\\\"}\",\"error\":null}", evt.Payload);
        Assert.Equal("completed", evt.Data!.StepType);
        Assert.Equal("Completed", evt.Data.Status);
        Assert.Equal("{\"token\":\"***\",\"value\":\"done\"}", evt.Data.Output);
        Assert.Equal("pending", evt.PublishStatus);
    }

    [Fact]
    public async Task Cancel()
    {
        var mediator = new FakeMediator((CancelAgentRunCommand command) =>
        {
            Assert.Equal("run-001", command.RunId);
            Assert.Equal("User requested stop", command.Reason);

            return new CancelAgentRunResult("run-001", "writer", "hello", null, "Cancelled", null, command.Reason);
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.Cancel(
            "run-001",
            new CancelAgentRunRequest("User requested stop"),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<CancelAgentRunResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("writer", response.AgentCode);
        Assert.Equal("Cancelled", response.Status);
        Assert.Equal("User requested stop", response.Reason);
    }

    [Fact]
    public async Task RequestHuman()
    {
        var mediator = new FakeMediator((RequestHumanAgentRunCommand command) =>
        {
            Assert.Equal("run-001", command.RunId);
            Assert.Equal("Need human help", command.Comment);
            Assert.Equal("step-123", command.SourceStepId);
            Assert.Equal("u-2", command.HumanOperatorId);
            Assert.Equal("Bob", command.HumanOperatorName);
            Assert.Equal("operator", command.HumanOperatorRole);

            return new RequestHumanAgentRunResult("run-001", "writer", "hello", null, "WaitingHuman", "human-wait-1");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.RequestHuman(
            "run-001",
            new RequestHumanAgentRunRequest("Need human help", "step-123", "u-2", "Bob", "operator"),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<RequestHumanAgentRunResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("WaitingHuman", response.Status);
        Assert.Equal("human-wait-1", response.WaitToken);
    }

    [Fact]
    public async Task RequestHuman_ShouldPreferAuthenticatedUserContextOverRequestBody()
    {
        var mediator = new FakeMediator((RequestHumanAgentRunCommand command) =>
        {
            Assert.Equal("claim-user", command.HumanOperatorId);
            Assert.Equal("Claim User", command.HumanOperatorName);
            Assert.Equal("reviewer", command.HumanOperatorRole);

            return new RequestHumanAgentRunResult("run-001", "writer", "hello", null, "WaitingHuman", "human-wait-1");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("claim-user", "Claim User", "reviewer")
                }
            }
        };

        var actionResult = await controller.RequestHuman(
            "run-001",
            new RequestHumanAgentRunRequest("Need human help", "step-123", "body-user", "Body User", "viewer"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task CompleteHuman()
    {
        var mediator = new FakeMediator((CompleteHumanAgentRunCommand command) =>
        {
            Assert.Equal("run-001", command.RunId);
            Assert.Equal("step-002", command.StepId);
            Assert.Equal("human-wait-1", command.WaitToken);
            Assert.Equal("Human supplied answer", command.HumanResult);
            Assert.Equal("Resolved manually", command.Comment);
            Assert.False(command.Terminate);
            Assert.Equal("u-2", command.HumanOperatorId);
            Assert.Equal("Bob", command.HumanOperatorName);
            Assert.Equal("operator", command.HumanOperatorRole);

            return new CompleteHumanAgentRunResult("run-001", "writer", "hello", null, "Running");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.CompleteHuman(
            "run-001",
            "step-002",
            new CompleteHumanAgentRunRequest("human-wait-1", "Human supplied answer", "Resolved manually", false, "u-2", "Bob", "operator"),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<CompleteHumanAgentRunResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("Running", response.Status);
    }

    [Fact]
    public async Task CompleteHuman_ShouldPreferAuthenticatedUserContextOverRequestBody()
    {
        var mediator = new FakeMediator((CompleteHumanAgentRunCommand command) =>
        {
            Assert.Equal("claim-user", command.HumanOperatorId);
            Assert.Equal("Claim User", command.HumanOperatorName);
            Assert.Equal("reviewer", command.HumanOperatorRole);

            return new CompleteHumanAgentRunResult("run-001", "writer", "hello", null, "Running");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("claim-user", "Claim User", "reviewer")
                }
            }
        };

        var actionResult = await controller.CompleteHuman(
            "run-001",
            "step-002",
            new CompleteHumanAgentRunRequest("human-wait-1", "Human supplied answer", "Resolved manually", false, "body-user", "Body User", "viewer"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task CompleteToolInvocation()
    {
        var mediator = new FakeMediator((CompleteToolInvocationCommand command) =>
        {
            Assert.Equal("run-001", command.RunId);
            Assert.Equal("invocation-004", command.InvocationId);
            Assert.Equal("wait-1", command.WaitToken);
            Assert.Equal("""{"done":true}""", command.ToolResult);
            Assert.Equal("tool-complete-1", command.IdempotencyKey);

            return new CompleteToolInvocationResult("run-001", "writer", "hello", null, "Running");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.CompleteToolInvocation(
            "run-001",
            "invocation-004",
            new CompleteToolInvocationRequest("wait-1", """{"done":true}"""),
            "tool-complete-1",
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<CompleteToolInvocationResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("writer", response.AgentCode);
        Assert.Equal("Running", response.Status);
    }

    [Fact]
    public async Task CompleteToolInvocation_ShouldAuthorizeWebhookBeforeDispatch()
    {
        var authorizer = new RecordingWebhookRequestAuthorizer();
        var toolInvocationRepository = new FakeToolInvocationRepository(new ToolInvocation
        {
            InvocationId = "invocation-004",
            ToolName = "weather"
        });
        var toolDefinitionRepository = new FakeToolDefinitionRepository(new ToolDefinition
        {
            ToolName = "weather",
            CallbackSecret = "tool-specific-secret"
        });
        var mediator = new FakeMediator((CompleteToolInvocationCommand command) =>
            new CompleteToolInvocationResult("run-001", "writer", "hello", null, "Running"));
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus(), authorizer, toolInvocationRepository, toolDefinitionRepository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Body = BuildJsonBody("""{"waitToken":"wait-1","toolResult":"{\"done\":true}"}""");

        var actionResult = await controller.CompleteToolInvocation(
            "run-001",
            "invocation-004",
            new CompleteToolInvocationRequest("wait-1", """{"done":true}"""),
            "tool-complete-1",
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Equal(1, authorizer.ToolAuthorizeCount);
        Assert.Equal(0, authorizer.ApprovalAuthorizeCount);
        Assert.Equal("tool-specific-secret", authorizer.LastToolCallbackSecret);
    }

    [Fact]
    public async Task CompleteApproval_ShouldPreferAuthenticatedUserContextOverRequestBody()
    {
        var mediator = new FakeMediator((CompleteApprovalCommand command) =>
        {
            Assert.Equal("run-001", command.RunId);
            Assert.Equal("approval-001", command.ApprovalId);
            Assert.Equal("Approved", command.Decision);
            Assert.Equal("claim-user", command.ApproverId);
            Assert.Equal("Claim User", command.ApproverName);
            Assert.Equal("reviewer", command.ApproverRole);
            Assert.Equal("Looks good", command.Comment);
            Assert.Equal("approval-complete-1", command.IdempotencyKey);

            return new CompleteApprovalResult("run-001", "writer", "hello", null, "Running", "Approved");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("claim-user", "Claim User", "reviewer")
                }
            }
        };

        var actionResult = await controller.CompleteApproval(
            "run-001",
            "approval-001",
            new CompleteApprovalRequest("Approved", "body-user", "Body User", "viewer", "Looks good"),
            "approval-complete-1",
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<CompleteApprovalResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("Running", response.Status);
        Assert.Equal("Approved", response.Decision);
    }

    [Fact]
    public async Task CompleteApproval_ShouldAuthorizeWebhookBeforeDispatch()
    {
        var authorizer = new RecordingWebhookRequestAuthorizer();
        var mediator = new FakeMediator((CompleteApprovalCommand command) =>
            new CompleteApprovalResult("run-001", "writer", "hello", null, "Running", "Approved"));
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus(), authorizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Body = BuildJsonBody("""{"decision":"Approved","comment":"Looks good"}""");

        var actionResult = await controller.CompleteApproval(
            "run-001",
            "approval-001",
            new CompleteApprovalRequest("Approved", "u-1", "Alice", "reviewer", "Looks good"),
            "approval-complete-1",
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Equal(0, authorizer.ToolAuthorizeCount);
        Assert.Equal(1, authorizer.ApprovalAuthorizeCount);
    }

    [Fact]
    public async Task Reject()
    {
        var mediator = new FakeMediator((RejectAgentRunStepCommand command) =>
        {
            Assert.Equal("run-001", command.RunId);
            Assert.Equal("step-001", command.StepId);
            Assert.Equal("Denied", command.Comment);
            Assert.Equal("u-1", command.ApproverId);
            Assert.Equal("Alice", command.ApproverName);
            Assert.Equal("admin", command.ApproverRole);

            return new RejectAgentRunStepResult("run-001", "writer", "hello", null, "Failed");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.Reject(
            "run-001",
            "step-001",
            new RejectAgentRunStepRequest("Denied", "u-1", "Alice", "admin"),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<RejectAgentRunStepResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("writer", response.AgentCode);
        Assert.Equal("Failed", response.Status);
    }

    [Fact]
    public async Task Approve()
    {
        var mediator = new FakeMediator((ApproveAgentRunStepCommand command) =>
        {
            Assert.Equal("run-001", command.RunId);
            Assert.Equal("step-001", command.StepId);
            Assert.Equal("u-1", command.ApproverId);
            Assert.Equal("Alice", command.ApproverName);
            Assert.Equal("admin", command.ApproverRole);
            Assert.Equal("Looks good", command.Comment);

            return new ApproveAgentRunStepResult("run-001", "writer", "hello", null, "Running");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.Approve(
            "run-001",
            "step-001",
            new ApproveAgentRunStepRequest("u-1", "Alice", "admin", "Looks good"),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApproveAgentRunStepResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("writer", response.AgentCode);
        Assert.Equal("Running", response.Status);
    }

    [Fact]
    public async Task Approve_ShouldPreferAuthenticatedUserContextOverRequestBody()
    {
        var mediator = new FakeMediator((ApproveAgentRunStepCommand command) =>
        {
            Assert.Equal("claim-user", command.ApproverId);
            Assert.Equal("Claim User", command.ApproverName);
            Assert.Equal("reviewer", command.ApproverRole);
            Assert.Equal("Looks good", command.Comment);

            return new ApproveAgentRunStepResult("run-001", "writer", "hello", null, "Running");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("claim-user", "Claim User", "reviewer")
                }
            }
        };

        var actionResult = await controller.Approve(
            "run-001",
            "step-001",
            new ApproveAgentRunStepRequest("body-user", "Body User", "body-role", "Looks good"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task Stream_ShouldWriteSseFormattedEvents_AndSetHeaders()
    {
        var eventBus = new RecordingEventBus(
        [
            new AgentRunEvent("run-001", "step", new AgentRunEventData(1, "tool_call", "Completed", "done"), "evt-1", 1, "Running", new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc)),
            new AgentRunEvent("run-001", "done", new AgentRunEventData(0, "completed", "Completed", "final output"), "evt-2", 2, "Completed", new DateTime(2026, 6, 8, 0, 0, 1, DateTimeKind.Utc))
        ]);
        var controller = new AgentRunsController(new FakeMediator((CreateAgentRunCommand _) => throw new NotSupportedException()), _mapper, eventBus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Response =
                    {
                        Body = new MemoryStream()
                    }
                }
            }
        };

        await controller.Stream("run-001", CancellationToken.None);

        Assert.Equal("text/event-stream", controller.Response.Headers["Content-Type"]);
        Assert.Equal("no-cache", controller.Response.Headers["Cache-Control"]);
        Assert.Equal("keep-alive", controller.Response.Headers["Connection"]);
        Assert.Equal("run-001", eventBus.SubscribedRunId);

        controller.Response.Body.Position = 0;
        using var reader = new StreamReader(controller.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Contains("id: 1\nevent: step\ndata: {\"eventId\":\"evt-1\",\"runId\":\"run-001\",\"seqNo\":1,\"eventType\":\"step\",\"runStatus\":\"Running\",\"occurredAt\":\"2026-06-08T00:00:00Z\",\"data\":{\"stepNo\":1,\"stepType\":\"tool_call\",\"status\":\"Completed\",\"output\":\"done\",\"error\":null,\"modelFailure\":null,\"toolFailure\":null}}\n\n", body);
        Assert.Contains("id: 2\nevent: done\ndata: {\"eventId\":\"evt-2\",\"runId\":\"run-001\",\"seqNo\":2,\"eventType\":\"done\",\"runStatus\":\"Completed\",\"occurredAt\":\"2026-06-08T00:00:01Z\",\"data\":{\"stepNo\":0,\"stepType\":\"completed\",\"status\":\"Completed\",\"output\":\"final output\",\"error\":null,\"modelFailure\":null,\"toolFailure\":null}}\n\n", body);
    }

    [Fact]
    public async Task Stream_ShouldMaskJsonOutputInSsePayload()
    {
        var eventBus = new RecordingEventBus(
        [
            new AgentRunEvent(
                "run-001",
                "done",
                new AgentRunEventData(0, "completed", "Completed", "{\"token\":\"secret-1\",\"value\":\"done\"}"),
                "evt-3",
                3,
                "Completed",
                new DateTime(2026, 6, 8, 0, 0, 2, DateTimeKind.Utc))
        ]);
        var controller = new AgentRunsController(new FakeMediator((CreateAgentRunCommand _) => throw new NotSupportedException()), _mapper, eventBus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Response =
                    {
                        Body = new MemoryStream()
                    }
                }
            }
        };

        await controller.Stream("run-001", CancellationToken.None);

        controller.Response.Body.Position = 0;
        using var reader = new StreamReader(controller.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Contains("\\u0022token\\u0022:\\u0022***\\u0022", body);
        Assert.DoesNotContain("secret-1", body);
    }

    [Fact]
    public async Task Stream_ShouldReplayEventsFromLastEventId_BeforeSubscribingToLiveEvents()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunEventsQuery query) =>
        {
            Assert.Equal("run-001", query.RunId);
            Assert.Equal(1, query.AfterSeqNo);

            return (IReadOnlyList<GetAgentRunEventsItem>)
            [
                new(
                    "evt-2",
                    "run-001",
                    2,
                    "step",
                    "Running",
                    "{\"stepNo\":2,\"stepType\":\"tool_call\",\"status\":\"Completed\",\"output\":\"replayed\"}",
                    new EventDataInfo(2, "tool_call", "Completed", "replayed", null, null, null),
                    "published",
                    now,
                    0,
                    now,
                    now,
                    now)
            ];
        });
        var eventBus = new RecordingEventBus(
        [
            new AgentRunEvent("run-001", "done", new AgentRunEventData(3, "completed", "Completed", "live"), "evt-3", 3, "Completed", now.AddSeconds(1))
        ]);
        var controller = new AgentRunsController(mediator, _mapper, eventBus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Response =
                    {
                        Body = new MemoryStream()
                    }
                }
            }
        };
        controller.Request.Headers["Last-Event-ID"] = "1";

        await controller.Stream("run-001", CancellationToken.None);

        Assert.Equal(1, eventBus.SubscribeCallCount);
        Assert.Equal(1, mediator.LastGetEventsQuery?.AfterSeqNo);

        controller.Response.Body.Position = 0;
        using var reader = new StreamReader(controller.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        var replayIndex = body.IndexOf("id: 2\nevent: step\ndata:", StringComparison.Ordinal);
        var liveIndex = body.IndexOf("id: 3\nevent: done\ndata:", StringComparison.Ordinal);
        Assert.True(replayIndex >= 0, "Expected replayed event to be present in SSE body.");
        Assert.True(liveIndex > replayIndex, "Expected live event to be emitted after replayed events.");
    }

    [Fact]
    public async Task Stream_ShouldReturnAfterReplay_WhenReplayEndsWithTerminalEvent()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunEventsQuery query) =>
            (IReadOnlyList<GetAgentRunEventsItem>)
            [
                new(
                    "evt-2",
                    "run-001",
                    2,
                    "done",
                    "Completed",
                    "{\"stepNo\":0,\"stepType\":\"completed\",\"status\":\"Completed\",\"output\":\"final\"}",
                    new EventDataInfo(0, "completed", "Completed", "final", null, null, null),
                    "published",
                    now,
                    0,
                    now,
                    now,
                    now)
            ]);
        var eventBus = new RecordingEventBus(
        [
            new AgentRunEvent("run-001", "done", new AgentRunEventData(3, "completed", "Completed", "live"), "evt-3", 3, "Completed", now.AddSeconds(1))
        ]);
        var controller = new AgentRunsController(mediator, _mapper, eventBus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Response =
                    {
                        Body = new MemoryStream()
                    }
                }
            }
        };
        controller.Request.Headers["Last-Event-ID"] = "1";

        await controller.Stream("run-001", CancellationToken.None);

        Assert.Equal(1, eventBus.SubscribeCallCount);

        controller.Response.Body.Position = 0;
        using var reader = new StreamReader(controller.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Contains("id: 2\nevent: done\ndata:", body);
        Assert.DoesNotContain("id: 3\nevent: done\ndata:", body);
    }

    [Fact]
    public async Task Stream_ShouldIgnoreInvalidLastEventIdHeader()
    {
        var eventBus = new RecordingEventBus(
        [
            new AgentRunEvent("run-001", "done", new AgentRunEventData(1, "completed", "Completed", "live"), "evt-1", 1, "Completed", new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc))
        ]);
        var controller = new AgentRunsController(new FakeMediator((CreateAgentRunCommand _) => throw new NotSupportedException()), _mapper, eventBus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Response =
                    {
                        Body = new MemoryStream()
                    }
                }
            }
        };
        controller.Request.Headers["Last-Event-ID"] = "not-a-number";

        await controller.Stream("run-001", CancellationToken.None);

        Assert.Equal(1, eventBus.SubscribeCallCount);

        controller.Response.Body.Position = 0;
        using var reader = new StreamReader(controller.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Contains("id: 1\nevent: done\ndata:", body);
    }

    [Fact]
    public async Task Stream_ShouldSkipBufferedDuplicateEvents_WhenReplayOverlapsWithLiveSubscription()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunEventsQuery query) =>
            (IReadOnlyList<GetAgentRunEventsItem>)
            [
                new(
                    "evt-2",
                    "run-001",
                    2,
                    "step",
                    "Running",
                    "{\"stepNo\":2,\"stepType\":\"tool_call\",\"status\":\"Completed\",\"output\":\"replayed\"}",
                    new EventDataInfo(2, "tool_call", "Completed", "replayed", null, null, null),
                    "published",
                    now,
                    0,
                    now,
                    now,
                    now)
            ]);
        var eventBus = new RecordingEventBus(
        [
            new AgentRunEvent("run-001", "step", new AgentRunEventData(2, "tool_call", "Completed", "duplicate"), "evt-2", 2, "Running", now),
            new AgentRunEvent("run-001", "done", new AgentRunEventData(3, "completed", "Completed", "live"), "evt-3", 3, "Completed", now.AddSeconds(1))
        ]);
        var controller = new AgentRunsController(mediator, _mapper, eventBus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Response =
                    {
                        Body = new MemoryStream()
                    }
                }
            }
        };
        controller.Request.Headers["Last-Event-ID"] = "1";

        await controller.Stream("run-001", CancellationToken.None);

        controller.Response.Body.Position = 0;
        using var reader = new StreamReader(controller.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(1, CountOccurrences(body, "id: 2\nevent: step\ndata:"));
        Assert.Equal(1, CountOccurrences(body, "id: 3\nevent: done\ndata:"));
        Assert.DoesNotContain("duplicate", body);
    }

    private static ClaimsPrincipal CreatePrincipal(
        string userId,
        string userName,
        string role,
        string? tenantId = null,
        string? sessionId = null)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.Role, role)
        };

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            claims.Add(new Claim("tenant_id", tenantId));
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            claims.Add(new Claim("session_id", sessionId));
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(
            claims,
            authenticationType: "TestAuth"));
    }

    private static MemoryStream BuildJsonBody(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    private sealed class NullEventBus : IAgentRunEventBus
    {
        public void Publish(AgentRunEvent evt) { }

        public IAgentRunEventSubscription Subscribe(string runId)
        {
            return new StaticEventSubscription([]);
        }

        public async IAsyncEnumerable<AgentRunEvent> SubscribeAsync(
            string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await using var subscription = Subscribe(runId);
            await foreach (var evt in subscription.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
    }

    private sealed class RecordingEventBus : IAgentRunEventBus
    {
        private readonly IReadOnlyList<AgentRunEvent> _events;

        public RecordingEventBus(IReadOnlyList<AgentRunEvent> events)
        {
            _events = events;
        }

        public string? SubscribedRunId { get; private set; }

        public int SubscribeCallCount { get; private set; }

        public void Publish(AgentRunEvent evt) { }

        public IAgentRunEventSubscription Subscribe(string runId)
        {
            SubscribeCallCount++;
            SubscribedRunId = runId;
            return new StaticEventSubscription(_events);
        }

        public async IAsyncEnumerable<AgentRunEvent> SubscribeAsync(
            string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await using var subscription = Subscribe(runId);
            await foreach (var evt in subscription.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
    }

    private sealed class StaticEventSubscription : IAgentRunEventSubscription
    {
        private readonly IReadOnlyList<AgentRunEvent> _events;

        public StaticEventSubscription(IReadOnlyList<AgentRunEvent> events)
        {
            _events = events;
        }

        public async IAsyncEnumerable<AgentRunEvent> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var evt in _events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return evt;
                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingWebhookRequestAuthorizer : IWebhookRequestAuthorizer
    {
        public int ToolAuthorizeCount { get; private set; }

        public int ApprovalAuthorizeCount { get; private set; }

        public string? LastToolCallbackSecret { get; private set; }

        public Task AuthorizeToolCallbackAsync(HttpRequest request, string? callbackSecret, CancellationToken cancellationToken)
        {
            ToolAuthorizeCount++;
            LastToolCallbackSecret = callbackSecret;
            return Task.CompletedTask;
        }

        public Task AuthorizeApprovalCallbackAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            ApprovalAuthorizeCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeToolInvocationRepository : IToolInvocationRepository
    {
        private readonly ToolInvocation? _toolInvocation;

        public FakeToolInvocationRepository(ToolInvocation? toolInvocation)
        {
            _toolInvocation = toolInvocation;
        }

        public Task AddAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ToolInvocation?> GetByInvocationIdAsync(string invocationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_toolInvocation?.InvocationId == invocationId ? _toolInvocation : null);
        }

        public Task<ToolInvocation?> GetPendingByRunIdAndStepIdAsync(string runId, string stepId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ToolInvocation>> ListByRunIdAsync(string runId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAgentRunRepository : IAgentRunRepository
    {
        private readonly AgentRun? _agentRun;

        public FakeAgentRunRepository(AgentRun? agentRun)
        {
            _agentRun = agentRun;
        }

        public Task AddAsync(AgentRun agentRun, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AgentRun?> GetByRunIdAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_agentRun?.RunId == runId ? _agentRun : null);
        }

        public Task<AgentRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AgentRun>> ListByParentRunIdAsync(string parentRunId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AgentRun?> GetLatestChildByParentRunIdAsync(string parentRunId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateAsync(AgentRun agentRun, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeToolDefinitionRepository : IToolDefinitionRepository
    {
        private readonly ToolDefinition? _toolDefinition;

        public FakeToolDefinitionRepository(ToolDefinition? toolDefinition)
        {
            _toolDefinition = toolDefinition;
        }

        public Task<ToolDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ToolDefinition?> GetByToolNameAsync(string toolName, CancellationToken cancellationToken)
        {
            return Task.FromResult(_toolDefinition?.ToolName == toolName ? _toolDefinition : null);
        }

        public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ToolDefinition>> GetEnabledAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsByToolNameAsync(string toolName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> AnyAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AddAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeMediator : IMediator
    {
        private readonly Func<object, object?> _handler;

        public GetAgentRunEventsQuery? LastGetEventsQuery { get; private set; }

        public FakeMediator(Func<RejectAgentRunStepCommand, RejectAgentRunStepResult> handler)
        {
            _handler = request => request is RejectAgentRunStepCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<ApproveAgentRunStepCommand, ApproveAgentRunStepResult> handler)
        {
            _handler = request => request is ApproveAgentRunStepCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<CreateAgentRunCommand, CreateAgentRunResult> handler)
        {
            _handler = request => request is CreateAgentRunCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<CancelAgentRunCommand, CancelAgentRunResult> handler)
        {
            _handler = request => request is CancelAgentRunCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<CompleteToolInvocationCommand, CompleteToolInvocationResult> handler)
        {
            _handler = request => request is CompleteToolInvocationCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<CompleteApprovalCommand, CompleteApprovalResult> handler)
        {
            _handler = request => request is CompleteApprovalCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<RequestHumanAgentRunCommand, RequestHumanAgentRunResult> handler)
        {
            _handler = request => request is RequestHumanAgentRunCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<CompleteHumanAgentRunCommand, CompleteHumanAgentRunResult> handler)
        {
            _handler = request => request is CompleteHumanAgentRunCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentRunByIdQuery, GetAgentRunByIdResult?> handler)
        {
            _handler = request => request is GetAgentRunByIdQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentRunStepsQuery, IReadOnlyList<GetAgentRunStepsItem>> handler)
        {
            _handler = request => request is GetAgentRunStepsQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentRunChildrenQuery, IReadOnlyList<GetAgentRunChildrenItem>> handler)
        {
            _handler = request => request is GetAgentRunChildrenQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentRunTreeQuery, GetAgentRunTreeItem?> handler)
        {
            _handler = request => request is GetAgentRunTreeQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentRunApprovalsQuery, IReadOnlyList<GetAgentRunApprovalsItem>> handler)
        {
            _handler = request => request is GetAgentRunApprovalsQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentRunEventsQuery, IReadOnlyList<GetAgentRunEventsItem>> handler)
        {
            _handler = request => request is GetAgentRunEventsQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetAgentRunEventsQuery getEventsQuery)
            {
                LastGetEventsQuery = getEventsQuery;
            }

            return Task.FromResult((TResponse)_handler(request)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = _handler(request!);
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_handler(request));
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            return Task.CompletedTask;
        }
    }
}
