using System.Text.Json;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Runtime;

public class AgentRunLoopTests
{
    private readonly IModelGateway _modelGateway = Substitute.For<IModelGateway>();
    private readonly IStepDecisionParser _stepDecisionParser = Substitute.For<IStepDecisionParser>();
    private readonly IToolExecutor _toolExecutor = Substitute.For<IToolExecutor>();
    private readonly IAgentStepRepository _agentStepRepository = Substitute.For<IAgentStepRepository>();
    private readonly IToolDefinitionRepository _toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
    private readonly IToolInvocationRepository _toolInvocationRepository = Substitute.For<IToolInvocationRepository>();
    private readonly IRouteRuleRepository _routeRuleRepository = Substitute.For<IRouteRuleRepository>();
    private readonly IRuntimeContextComposer _runtimeContextComposer = Substitute.For<IRuntimeContextComposer>();

    [Fact]
    public async Task ExecuteAsync_ShouldCompleteRunAfterToolCall()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var events = new List<AgentRunEvent>();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("tool-decision"),
                new GenerateTextResult("final-answer"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("Hello from tool"));

        _toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "sunny"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal("Hello from tool", completed.Output);

        await _toolExecutor.Received(1).ExecuteAsync(
            "weather",
            "{\"city\":\"Shanghai\"}",
            Arg.Is<ToolExecutionContext>(toolContext =>
                toolContext.RunId == "run-001" &&
                toolContext.AgentCode == "writer" &&
                toolContext.UserInput == "hello"),
            Arg.Any<CancellationToken>());

        await _modelGateway.Received(2).GenerateTextAsync(
            Arg.Any<GenerateTextRequest>(),
            Arg.Any<CancellationToken>());

        await _modelGateway.Received(1).GenerateTextAsync(
            Arg.Is<GenerateTextRequest>(request =>
                request.Input.Contains("Original user input:\nhello") &&
                request.Input.Contains("Tool called:\nweather") &&
                request.Input.Contains("Tool result:\nsunny")),
            Arg.Any<CancellationToken>());

        await _agentStepRepository.Received(3).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepNo == 4 &&
                step.StepType == "tool_call" &&
                step.Status == "Completed" &&
                step.InputPayload == "{\"city\":\"Shanghai\"}" &&
                step.OutputPayload == "sunny"),
            Arg.Any<CancellationToken>());
        await _toolInvocationRepository.Received(1).AddAsync(
            Arg.Is<ToolInvocation>(invocation =>
                invocation.RunId == "run-001" &&
                invocation.ToolName == "weather" &&
                invocation.Mode == "sync" &&
                invocation.Status == "Completed" &&
                invocation.InputPayload == "{\"city\":\"Shanghai\"}" &&
                invocation.OutputPayload == "sunny" &&
                !string.IsNullOrWhiteSpace(invocation.InvocationId)),
            Arg.Any<CancellationToken>());

        Assert.Collection(events,
            evt => AssertEvent(evt, "step", 3, "model_call", "Completed", "tool-decision"),
            evt => AssertEvent(evt, "step", 5, "model_call", "Completed", "final-answer"));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSuspendRun_WhenToolReturnsPending()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var events = new List<AgentRunEvent>();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        _toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Pending("weather", "wait-123"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        var suspended = Assert.IsType<AgentLoopSuspended>(result);
        Assert.Equal("wait-123", suspended.WaitToken);
        Assert.Equal(4, suspended.SuspendedAtStepNo);
        Assert.False(string.IsNullOrWhiteSpace(suspended.StepId));
        Assert.False(string.IsNullOrWhiteSpace(suspended.InvocationId));
        Assert.Equal("weather", suspended.ToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", suspended.ToolInput);

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepNo == 4 &&
                step.StepType == "tool_call" &&
                step.Status == "Pending" &&
                step.InputPayload == "{\"city\":\"Shanghai\"}" &&
                step.OutputPayload == null),
            Arg.Any<CancellationToken>());
        await _toolInvocationRepository.Received(1).AddAsync(
            Arg.Is<ToolInvocation>(invocation =>
                invocation.InvocationId == suspended.InvocationId &&
                invocation.RunId == "run-001" &&
                invocation.StepId == suspended.StepId &&
                invocation.ToolName == "weather" &&
                invocation.Mode == "async" &&
                invocation.Status == "Pending" &&
                invocation.InputPayload == "{\"city\":\"Shanghai\"}" &&
                invocation.OutputPayload == null &&
                invocation.CallbackToken == "wait-123"),
            Arg.Any<CancellationToken>());

        Assert.Single(events);
        AssertEvent(events[0], "step", 3, "model_call", "Completed", "tool-decision");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWaitForApproval_WhenToolRequiresApproval()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var events = new List<AgentRunEvent>();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Description = "Weather tool",
                Enabled = true,
                SideEffectLevel = "internal_write"
            });

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        var waiting = Assert.IsType<AgentLoopWaitingApproval>(result);
        Assert.Equal(4, waiting.SuspendedAtStepNo);
        Assert.Equal("weather", waiting.ToolName);
        Assert.Equal("internal_write", waiting.SideEffectLevel);
        Assert.False(string.IsNullOrWhiteSpace(waiting.WaitToken));
        Assert.False(string.IsNullOrWhiteSpace(waiting.StepId));

        await _toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepId == waiting.StepId &&
                step.StepNo == 4 &&
                step.StepType == "tool_call" &&
                step.Status == "Pending" &&
                step.InputPayload == "{\"city\":\"Shanghai\"}" &&
                step.DecisionPayload != null),
            Arg.Any<CancellationToken>());

        var pendingStep = _agentStepRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Last(step => step.StepType == "tool_call");
        var approvalPayload = ApprovalPayloadSerializer.Parse(pendingStep.DecisionPayload!);
        Assert.Equal("approval", approvalPayload.WaitType);
        Assert.Equal("weather", approvalPayload.ToolName);
        Assert.Equal(ApprovalDecisions.Pending, approvalPayload.Decision);

        Assert.Single(events);
        AssertEvent(events[0], "step", 3, "model_call", "Completed", "tool-decision");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolIsExplicitlyDenied()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(deniedTools: "[\"weather\"]");

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentRunLoop.ExecuteAsync(
                context,
                resolvedDefinition,
                _modelGateway,
                _stepDecisionParser,
                _toolExecutor,
                _agentStepRepository,
                _toolDefinitionRepository,
                _toolInvocationRepository,
                CancellationToken.None));

        Assert.Equal("Tool 'weather' is denied for agent definition 'writer'.", exception.Message);
        await _toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWaitForApproval_WhenToolIsDestructive_ByDefault()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Description = "Weather tool",
                Enabled = true,
                SideEffectLevel = "destructive"
            });

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var waiting = Assert.IsType<AgentLoopWaitingApproval>(result);
        Assert.Equal("destructive", waiting.SideEffectLevel);
        await _toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWaitForApproval_WhenPolicyMarksCustomRiskLevel()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var policy = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = ["destructive"]
        };

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Description = "Weather tool",
                Enabled = true,
                SideEffectLevel = "destructive"
            });

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            approvalPolicyOptions: policy);

        var waiting = Assert.IsType<AgentLoopWaitingApproval>(result);
        Assert.Equal("destructive", waiting.SideEffectLevel);
        await _toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWaitForApproval_WhenToolInputMatchesParameterRule()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var policy = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = [],
            ParameterApprovalRules =
            [
                new ApprovalParameterRule
                {
                    ToolName = "weather",
                    InputPath = "city",
                    ExpectedValue = "Shanghai",
                    OverrideSideEffectLevel = "destructive"
                }
            ]
        };

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Description = "Weather tool",
                Enabled = true,
                SideEffectLevel = "read_only"
            });

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            approvalPolicyOptions: policy);

        var waiting = Assert.IsType<AgentLoopWaitingApproval>(result);
        Assert.Equal("destructive", waiting.SideEffectLevel);
        await _toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());

        var pendingStep = _agentStepRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Last(step => step.StepType == "tool_call");
        var approvalPayload = ApprovalPayloadSerializer.Parse(pendingStep.DecisionPayload!);
        Assert.Equal("destructive", approvalPayload.SideEffectLevel);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReuseCompletedToolInvocation_WhenIdempotencyPolicyEnabled()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var events = new List<AgentRunEvent>();
        var existingInvocation = AgentRunLoop.CreateToolInvocation(
            "invocation-existing",
            "run-001",
            "step-existing",
            "weather",
            "sync",
            "Completed",
            "{\"city\":\"Shanghai\"}",
            "sunny",
            null,
            "tool-idempotency-key",
            string.Empty,
            DateTime.UtcNow.AddMinutes(-2),
            DateTime.UtcNow.AddMinutes(-2).AddMilliseconds(25),
            DateTime.UtcNow.AddMinutes(-2).AddMilliseconds(25));

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("tool-decision"),
                new GenerateTextResult("final-answer"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("Hello from reused tool"));

        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Enabled = true,
                IdempotencyPolicy = "idempotent"
            });
        _toolInvocationRepository.ListByRunIdAsync("run-001", Arg.Any<CancellationToken>())
            .Returns([existingInvocation]);

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal("Hello from reused tool", completed.Output);

        await _toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());
        await _toolInvocationRepository.Received(1).AddAsync(
            Arg.Is<ToolInvocation>(invocation =>
                invocation.RunId == "run-001" &&
                invocation.ToolName == "weather" &&
                invocation.Mode == "reused" &&
                invocation.Status == "Completed" &&
                invocation.InputPayload == "{\"city\":\"Shanghai\"}" &&
                invocation.OutputPayload == "sunny" &&
                invocation.IdempotencyKey == "tool-idempotency-key"),
            Arg.Any<CancellationToken>());
        await _modelGateway.Received(1).GenerateTextAsync(
            Arg.Is<GenerateTextRequest>(request =>
                request.Input.Contains("Tool called:\nweather") &&
                request.Input.Contains("Tool result:\nsunny")),
            Arg.Any<CancellationToken>());

        Assert.Collection(events,
            evt => AssertEvent(evt, "step", 3, "model_call", "Completed", "tool-decision"),
            evt => AssertEvent(evt, "step", 5, "model_call", "Completed", "final-answer"));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReusePendingToolInvocation_WhenIdempotencyPolicyEnabled()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var events = new List<AgentRunEvent>();
        var existingPendingStep = AgentRunLoop.CreateStep(
            "run-001",
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(-1)) with
        {
            DecisionPayload = JsonSerializer.Serialize(new
            {
                toolName = "weather"
            })
        };
        var existingPendingInvocation = AgentRunLoop.CreateToolInvocation(
            "invocation-pending",
            "run-001",
            existingPendingStep.StepId,
            "weather",
            "async",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            "tool-idempotency-key",
            "wait-existing",
            DateTime.UtcNow.AddMinutes(-1),
            null,
            DateTime.UtcNow.AddMinutes(-1));

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));

        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Enabled = true,
                IdempotencyPolicy = "idempotent"
            });
        _toolInvocationRepository.ListByRunIdAsync("run-001", Arg.Any<CancellationToken>())
            .Returns([existingPendingInvocation]);
        _agentStepRepository.GetByStepIdAsync(existingPendingStep.StepId, Arg.Any<CancellationToken>())
            .Returns(existingPendingStep);

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        var suspended = Assert.IsType<AgentLoopSuspended>(result);
        Assert.Equal("wait-existing", suspended.WaitToken);
        Assert.Equal(existingPendingStep.StepNo, suspended.SuspendedAtStepNo);
        Assert.Equal(existingPendingStep.StepId, suspended.StepId);
        Assert.Equal("invocation-pending", suspended.InvocationId);
        Assert.Equal("weather", suspended.ToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", suspended.ToolInput);

        await _toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepNo == 3 &&
                step.StepType == "model_call" &&
                step.Status == "Completed"),
            Arg.Any<CancellationToken>());
        await _toolInvocationRepository.DidNotReceive().AddAsync(
            Arg.Any<ToolInvocation>(),
            Arg.Any<CancellationToken>());

        Assert.Single(events);
        AssertEvent(events[0], "step", 3, "model_call", "Completed", "tool-decision");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCreateFailedToolStepAndInvocation_WhenToolExecutionThrows()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var events = new List<AgentRunEvent>();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Enabled = true,
                CompensationPolicy = "{\"mode\":\"manual\"}"
            });

        _toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ToolExecutionResult>>(_ => throw new InvalidOperationException("Output for tool 'weather' at '$' must be string."));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        var waitingHuman = Assert.IsType<AgentLoopWaitingHuman>(result);
        Assert.Equal(5, waitingHuman.SuspendedAtStepNo);
        Assert.Equal("weather", waitingHuman.ToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", waitingHuman.ToolInput);
        Assert.Contains("manual compensation", waitingHuman.Comment, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(waitingHuman.SourceStepId));
        Assert.True(ToolFailurePayloadSerializer.TryParse(waitingHuman.SourceToolOutput, out var loopFailurePayload));
        Assert.NotNull(loopFailurePayload);
        Assert.Equal("weather", loopFailurePayload!.ToolName);
        Assert.Equal("execution", loopFailurePayload.Stage);
        Assert.Equal("manual", loopFailurePayload.Compensation!.Mode);
        Assert.False(string.IsNullOrWhiteSpace(waitingHuman.WaitToken));
        Assert.False(string.IsNullOrWhiteSpace(waitingHuman.SourceInvocationId));

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepNo == 4 &&
                step.StepType == "tool_call" &&
                step.Status == "Failed" &&
                step.InputPayload == "{\"city\":\"Shanghai\"}" &&
                step.ErrorPayload != null &&
                step.ErrorPayload.Contains("\"toolName\":\"weather\"")),
            Arg.Any<CancellationToken>());
        await _toolInvocationRepository.Received(1).AddAsync(
            Arg.Is<ToolInvocation>(invocation =>
                invocation.RunId == "run-001" &&
                invocation.ToolName == "weather" &&
                invocation.Mode == "sync" &&
                invocation.Status == "Failed" &&
                invocation.InputPayload == "{\"city\":\"Shanghai\"}" &&
                invocation.ErrorPayload != null &&
                invocation.ErrorPayload.Contains("\"toolName\":\"weather\"") &&
                invocation.ErrorPayload.Contains("\"compensation\":{\"mode\":\"manual\"}")),
            Arg.Any<CancellationToken>());

        Assert.Collection(events,
            evt => AssertEvent(evt, "step", 3, "model_call", "Completed", "tool-decision"),
            evt =>
            {
                Assert.Equal("step", evt.EventType);
                Assert.Equal(4, evt.Data.StepNo);
                Assert.Equal("tool_call", evt.Data.StepType);
                Assert.Equal("Failed", evt.Data.Status);
                Assert.Contains("\"toolName\":\"weather\"", evt.Data.Error);
            });
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCreateFailedToolStepAndInvocation_WhenToolReturnsFailedResult()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var events = new List<AgentRunEvent>();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("tool-decision"));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(new ToolDefinition
            {
                Id = "tool-1",
                ToolName = "weather",
                DisplayName = "Weather",
                Enabled = true,
                CompensationPolicy = "{\"mode\":\"manual\"}"
            });

        _toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Failed("weather", "tool backend crashed"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        var waitingHuman = Assert.IsType<AgentLoopWaitingHuman>(result);
        Assert.Equal(5, waitingHuman.SuspendedAtStepNo);
        Assert.Equal("weather", waitingHuman.ToolName);
        Assert.Equal("{\"city\":\"Shanghai\"}", waitingHuman.ToolInput);
        Assert.Contains("manual compensation", waitingHuman.Comment, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(waitingHuman.SourceStepId));
        Assert.True(ToolFailurePayloadSerializer.TryParse(waitingHuman.SourceToolOutput, out var failedPayload));
        Assert.NotNull(failedPayload);
        Assert.Equal("manual", failedPayload!.Compensation!.Mode);
        Assert.False(string.IsNullOrWhiteSpace(waitingHuman.WaitToken));
        Assert.False(string.IsNullOrWhiteSpace(waitingHuman.SourceInvocationId));

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepNo == 4 &&
                step.StepType == "tool_call" &&
                step.Status == "Failed" &&
                step.ErrorPayload != null &&
                step.ErrorPayload.Contains("\"message\":\"tool backend crashed\"")),
            Arg.Any<CancellationToken>());
        await _toolInvocationRepository.Received(1).AddAsync(
            Arg.Is<ToolInvocation>(invocation =>
                invocation.ToolName == "weather" &&
                invocation.Status == "Failed" &&
                invocation.ErrorPayload != null &&
                invocation.ErrorPayload.Contains("\"message\":\"tool backend crashed\"") &&
                invocation.ErrorPayload.Contains("\"compensation\":{\"mode\":\"manual\"}")),
            Arg.Any<CancellationToken>());

        Assert.Collection(events,
            evt => AssertEvent(evt, "step", 3, "model_call", "Completed", "tool-decision"),
            evt =>
            {
                Assert.Equal("step", evt.EventType);
                Assert.Equal("Failed", evt.Data.Status);
                Assert.Contains("\"message\":\"tool backend crashed\"", evt.Data.Error);
            });
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseComposedInput_WhenRuntimeContextComposerProvided()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        _runtimeContextComposer.ComposeModelInputAsync(context, resolvedDefinition, Arg.Any<CancellationToken>())
            .Returns(new RuntimeContextComposition("composed context"));
        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("done"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            runtimeContextComposer: _runtimeContextComposer);

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal("done", completed.Output);
        await _modelGateway.Received(1).GenerateTextAsync(
            Arg.Is<GenerateTextRequest>(request => request.Input == "composed context"),
            Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepType == "model_call" &&
                step.InputPayload == "composed context"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAppendKnowledgeReferences_ToFinalResponse()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        _runtimeContextComposer.ComposeModelInputAsync(context, resolvedDefinition, Arg.Any<CancellationToken>())
            .Returns(new RuntimeContextComposition(
                """
                Current user input:
                hello

                Reference knowledge:
                [1] Flights can be changed within 24 hours.
                Citation: score=3; source=faq/doc-1#1; chunk=1
                Source: faq/doc-1#1
                """));
        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("done"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            runtimeContextComposer: _runtimeContextComposer);

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal(
            """
            done

            References:
            [1] faq/doc-1#1 (score=3; source=faq/doc-1#1; chunk=1)
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            completed.Output.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotAppendKnowledgeReferences_WhenContextPolicyDisablesCitations()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(contextPolicy: "{\"citations\":false}");
        _runtimeContextComposer.ComposeModelInputAsync(context, resolvedDefinition, Arg.Any<CancellationToken>())
            .Returns(new RuntimeContextComposition(
                """
                Current user input:
                hello

                Reference knowledge:
                [1] Flights can be changed within 24 hours.
                Citation: score=3; source=faq/doc-1#1; chunk=1
                Source: faq/doc-1#1
                """));
        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("done"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            runtimeContextComposer: _runtimeContextComposer);

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal("done", completed.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnCompletedOutput_ForRunLevelOutputValidation()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(outputSchema: "{\"type\":\"object\",\"required\":[\"answer\"]}");

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("{\"answer\":\"done\"}"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal("{\"answer\":\"done\"}", completed.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWaitForHandoff_WhenDecisionRequestsAllowedTargetAgent()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(allowedHandoffs: "[\"support_agent\"]");

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        _stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please handle refund", "delegate_and_wait"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var waiting = Assert.IsType<AgentLoopWaitingHandoff>(result);
        Assert.Equal("support_agent", waiting.TargetAgent);
        Assert.Equal("please handle refund", waiting.HandoffInput);
        Assert.Equal("delegate_and_wait", waiting.HandoffMode);
        Assert.Equal(4, waiting.SuspendedAtStepNo);
        Assert.False(string.IsNullOrWhiteSpace(waiting.WaitToken));
        Assert.False(string.IsNullOrWhiteSpace(waiting.StepId));
        Assert.False(string.IsNullOrWhiteSpace(waiting.ChildRunId));

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepId == waiting.StepId &&
                step.StepNo == 4 &&
                step.StepType == "handoff" &&
                step.Status == "Pending" &&
                step.InputPayload == "please handle refund" &&
                step.DecisionPayload != null &&
                step.DecisionPayload.Contains("\"ChildRunId\":\"" + waiting.ChildRunId + "\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWaitForRouteOnlyHandoff_WhenDecisionRequestsAllowedTargetAgent()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(allowedHandoffs: "[\"support_agent\"]");

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        _stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please handle refund", "route_only"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var waiting = Assert.IsType<AgentLoopWaitingHandoff>(result);
        Assert.Equal("support_agent", waiting.TargetAgent);
        Assert.Equal("please handle refund", waiting.HandoffInput);
        Assert.Equal("route_only", waiting.HandoffMode);
        Assert.Equal(4, waiting.SuspendedAtStepNo);
        Assert.False(string.IsNullOrWhiteSpace(waiting.WaitToken));
        Assert.False(string.IsNullOrWhiteSpace(waiting.StepId));
        Assert.False(string.IsNullOrWhiteSpace(waiting.ChildRunId));

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepId == waiting.StepId &&
                step.StepNo == 4 &&
                step.StepType == "handoff" &&
                step.Status == "Pending" &&
                step.InputPayload == "please handle refund" &&
                step.DecisionPayload != null &&
                step.DecisionPayload.Contains("\"Mode\":\"route_only\"") &&
                step.DecisionPayload.Contains("\"ChildRunId\":\"" + waiting.ChildRunId + "\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWaitForDelegateAndMergeHandoff_WhenDecisionRequestsAllowedTargetAgent()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(allowedHandoffs: "[\"support_agent\"]");

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        _stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please handle refund", "delegate_and_merge", mergeStrategy: "all_results"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var waiting = Assert.IsType<AgentLoopWaitingHandoff>(result);
        Assert.Equal("support_agent", waiting.TargetAgent);
        Assert.Equal("please handle refund", waiting.HandoffInput);
        Assert.Equal("delegate_and_merge", waiting.HandoffMode);
        Assert.Equal("all_results", waiting.MergeStrategy);
        Assert.Equal(4, waiting.SuspendedAtStepNo);
        Assert.False(string.IsNullOrWhiteSpace(waiting.WaitToken));
        Assert.False(string.IsNullOrWhiteSpace(waiting.StepId));
        Assert.False(string.IsNullOrWhiteSpace(waiting.ChildRunId));

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepId == waiting.StepId &&
                step.StepNo == 4 &&
                step.StepType == "handoff" &&
                step.Status == "Pending" &&
                step.InputPayload == "please handle refund" &&
                step.DecisionPayload != null &&
                step.DecisionPayload.Contains("\"Mode\":\"delegate_and_merge\"") &&
                step.DecisionPayload.Contains("\"MergeStrategy\":\"all_results\"") &&
                step.DecisionPayload.Contains("\"ChildRunId\":\"" + waiting.ChildRunId + "\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectHandoff_WhenTargetAgentNotAllowed()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(allowedHandoffs: "[\"support_agent\"]");

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        _stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("finance_agent", "please handle refund", "delegate_and_wait"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentRunLoop.ExecuteAsync(
                context,
                resolvedDefinition,
                _modelGateway,
                _stepDecisionParser,
                _toolExecutor,
                _agentStepRepository,
                _toolDefinitionRepository,
                _toolInvocationRepository,
                CancellationToken.None));

        Assert.Equal("Handoff target 'finance_agent' is not allowed for agent definition 'writer'.", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseRouteRuleModeAndPersistRuleMetadata_WhenHandoffModeIsOmitted()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(allowedHandoffs: "[\"support_agent\"]");

        _routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Support Route",
                    Priority = 10,
                    MatchType = "intent",
                    HandoffMode = "route_only",
                    ContextScope = "{\"mode\":\"summary_only\"}",
                    MemoryScope = "{\"mode\":\"read_only\"}",
                    ToolScope = "{\"inherit\":false}",
                    KnowledgeScope = "{\"sources\":[\"faq\"]}",
                    ApprovalRequired = true,
                    Enabled = true
                }
            ]);
        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        _stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please handle refund", null));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            routeRuleRepository: _routeRuleRepository);

        var waiting = Assert.IsType<AgentLoopWaitingApproval>(result);
        Assert.Equal("handoff", waiting.StepType);
        Assert.Equal("support_agent", waiting.ToolName);
        Assert.Equal("internal_write", waiting.SideEffectLevel);

        await _routeRuleRepository.Received(1).GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepType == "handoff" &&
                step.Status == "Pending" &&
                step.DecisionPayload != null &&
                step.DecisionPayload.Contains("\"Mode\":\"route_only\"") &&
                step.DecisionPayload.Contains("\"RouteRuleId\":\"rule-1\"") &&
                step.DecisionPayload.Contains("\"ContextScope\":\"{\\u0022mode\\u0022:\\u0022summary_only\\u0022}\"") &&
                step.DecisionPayload.Contains("\"MemoryScope\":\"{\\u0022mode\\u0022:\\u0022read_only\\u0022}\"") &&
                step.DecisionPayload.Contains("\"ToolScope\":\"{\\u0022inherit\\u0022:false}\"") &&
                step.DecisionPayload.Contains("\"KnowledgeScope\":\"{\\u0022sources\\u0022:[\\u0022faq\\u0022]}\"") &&
                step.DecisionPayload.Contains("\"ApprovalRequired\":true")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseRouteRuleMergeStrategy_WhenModelDoesNotProvideOne()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(allowedHandoffs: "[\"support_agent\"]");

        _routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-merge-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Support Merge Route",
                    Priority = 10,
                    MatchType = "intent",
                    HandoffMode = "delegate_and_merge",
                    MergeStrategy = "first_success",
                    ApprovalRequired = false,
                    Enabled = true
                }
            ]);
        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        _stepDecisionParser.Parse("handoff-decision")
            .Returns(StepDecision.Handoff("support_agent", "please handle refund", "delegate_and_merge"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            routeRuleRepository: _routeRuleRepository);

        var waiting = Assert.IsType<AgentLoopWaitingHandoff>(result);
        Assert.Equal("delegate_and_merge", waiting.HandoffMode);
        Assert.Equal("first_success", waiting.MergeStrategy);

        var pendingStep = _agentStepRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Single(step => step.StepType == "handoff");
        var payload = HandoffPayloadSerializer.Parse(pendingStep.DecisionPayload);

        Assert.Equal("rule-merge-1", payload.RouteRuleId);
        Assert.Equal("first_success", payload.MergeStrategy);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAutomaticallyRoute_WhenRoutingPolicyIsHandoffFirst_AndRouteRuleMatchesInput()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(
            allowedHandoffs: "[\"support_agent\"]",
            routingPolicy: "{\"strategy\":\"handoff-first\"}");

        _routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Refund Route",
                    Priority = 10,
                    MatchType = "intent",
                    MatchExpression = "{\"intent\":\"refund\"}",
                    HandoffMode = "delegate_and_wait",
                    ContextScope = "{\"mode\":\"summary_only\"}",
                    MemoryScope = "{\"mode\":\"read_only\"}",
                    ToolScope = "{\"allowed\":[\"faq_search\"]}",
                    KnowledgeScope = "{\"allowed\":[\"faq\"]}",
                    ApprovalRequired = false,
                    Enabled = true
                }
            ]);

        var result = await AgentRunLoop.ExecuteAsync(
            context with { CurrentInput = "User asks for a refund on order #123" },
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            routeRuleRepository: _routeRuleRepository);

        var waiting = Assert.IsType<AgentLoopWaitingHandoff>(result);
        Assert.Equal("support_agent", waiting.TargetAgent);
        Assert.Equal("User asks for a refund on order #123", waiting.HandoffInput);
        Assert.Equal("delegate_and_wait", waiting.HandoffMode);

        await _modelGateway.DidNotReceive().GenerateTextAsync(
            Arg.Any<GenerateTextRequest>(),
            Arg.Any<CancellationToken>());

        var pendingStep = _agentStepRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Single(step => step.StepType == "handoff");
        var payload = HandoffPayloadSerializer.Parse(pendingStep.DecisionPayload);

        Assert.Equal("rule-1", payload.RouteRuleId);
        Assert.Equal("Matched route rule 'Refund Route'.", payload.Reason);
        Assert.Equal("{\"mode\":\"summary_only\"}", payload.ContextScope);
        Assert.Equal("{\"mode\":\"read_only\"}", payload.MemoryScope);
        Assert.Equal("{\"allowed\":[\"faq_search\"]}", payload.ToolScope);
        Assert.Equal("{\"allowed\":[\"faq\"]}", payload.KnowledgeScope);
        Assert.False(payload.ApprovalRequired);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallBackToModelDecision_WhenNoAutomaticRouteRuleMatches()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(
            allowedHandoffs: "[\"support_agent\"]",
            routingPolicy: "{\"strategy\":\"handoff-first\"}");

        _routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Refund Route",
                    Priority = 10,
                    MatchType = "intent",
                    MatchExpression = "{\"intent\":\"refund\"}",
                    HandoffMode = "route_only",
                    Enabled = true
                }
            ]);
        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("Handled by primary agent"));

        var result = await AgentRunLoop.ExecuteAsync(
            context with { CurrentInput = "User asks about office hours" },
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            routeRuleRepository: _routeRuleRepository);

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal("Handled by primary agent", completed.Output);

        await _modelGateway.Received(1).GenerateTextAsync(
            Arg.Any<GenerateTextRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRequireAllRouteTerms_WhenAutomaticRouteRuleUsesAllExpression()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(
            allowedHandoffs: "[\"support_agent\"]",
            routingPolicy: "{\"strategy\":\"handoff-first\"}");

        _routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Refund Order Route",
                    Priority = 10,
                    MatchType = "keyword",
                    MatchExpression = "{\"all\":[\"refund\",\"order\"]}",
                    HandoffMode = "route_only",
                    Enabled = true
                }
            ]);
        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("Handled by primary agent"));

        var result = await AgentRunLoop.ExecuteAsync(
            context with { CurrentInput = "User asks for a refund" },
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            routeRuleRepository: _routeRuleRepository);

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal("Handled by primary agent", completed.Output);

        await _modelGateway.Received(1).GenerateTextAsync(
            Arg.Any<GenerateTextRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPickHighestPriorityAutomaticRouteRule_WhenMultipleRulesMatch()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(
            allowedHandoffs: "[\"support_agent\",\"finance_agent\"]",
            routingPolicy: "{\"strategy\":\"handoff-first\"}");

        _routeRuleRepository.GetByAgentDefinitionVersionIdAsync("ver-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new RouteRule
                {
                    Id = "rule-2",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "finance_agent",
                    RuleName = "Finance Route",
                    Priority = 20,
                    MatchType = "keyword",
                    MatchExpression = "{\"any\":[\"refund\",\"invoice\"]}",
                    HandoffMode = "route_only",
                    Enabled = true
                },
                new RouteRule
                {
                    Id = "rule-1",
                    AgentDefinitionVersionId = "ver-1",
                    SourceAgentCode = "writer",
                    TargetAgentCode = "support_agent",
                    RuleName = "Support Route",
                    Priority = 10,
                    MatchType = "keyword",
                    MatchExpression = "{\"keyword\":\"refund\"}",
                    HandoffMode = "delegate_and_wait",
                    Enabled = true
                }
            ]);

        var result = await AgentRunLoop.ExecuteAsync(
            context with { CurrentInput = "User asks for refund status" },
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            routeRuleRepository: _routeRuleRepository);

        var waiting = Assert.IsType<AgentLoopWaitingHandoff>(result);
        Assert.Equal("support_agent", waiting.TargetAgent);
        Assert.Equal("delegate_and_wait", waiting.HandoffMode);

        await _modelGateway.DidNotReceive().GenerateTextAsync(
            Arg.Any<GenerateTextRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPersistRouteDecisionMetadata_WhenHandoffDecisionIncludesRouteFields()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition(allowedHandoffs: "[\"support_agent\"]");

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("handoff-decision"));
        _stepDecisionParser.Parse("handoff-decision")
            .Returns(
                StepDecision.Handoff(
                    "support_agent",
                    "please handle refund",
                    "delegate_and_wait",
                    reason: "Route to refund specialist",
                    confidence: 0.91,
                    contextOverrides: "{\"mode\":\"summary_only\"}",
                    memoryOverrides: "{\"mode\":\"read_only\"}",
                    toolOverrides: "{\"allowed\":[\"faq_search\"]}",
                    knowledgeOverrides: "{\"allowed\":[\"faq\"]}",
                    approvalRequired: true));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var waiting = Assert.IsType<AgentLoopWaitingApproval>(result);
        Assert.Equal("handoff", waiting.StepType);
        Assert.Equal("support_agent", waiting.ToolName);
        Assert.Equal("please handle refund", waiting.ToolInput);

        var pendingStep = _agentStepRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Last(step => step.StepType == "handoff");
        var payload = HandoffPayloadSerializer.Parse(pendingStep.DecisionPayload);

        Assert.Equal("Route to refund specialist", payload.Reason);
        Assert.Equal(0.91, payload.Confidence);
        Assert.Equal("{\"mode\":\"summary_only\"}", payload.ContextOverrides);
        Assert.Equal("{\"mode\":\"read_only\"}", payload.MemoryOverrides);
        Assert.Equal("{\"allowed\":[\"faq_search\"]}", payload.ToolOverrides);
        Assert.Equal("{\"allowed\":[\"faq\"]}", payload.KnowledgeOverrides);
        Assert.True(payload.ApprovalRequired);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnWaitingHuman_WhenDecisionRequestsHuman()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("human-decision"));
        _stepDecisionParser.Parse("human-decision")
            .Returns(StepDecision.RequestHuman("Need manual confirmation."));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var waiting = Assert.IsType<AgentLoopWaitingHuman>(result);
        Assert.Equal(4, waiting.SuspendedAtStepNo);
        Assert.Equal("Need manual confirmation.", waiting.Comment);
        Assert.Equal("run", waiting.SourceType);
        Assert.False(waiting.ContinueAsToolResult);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPersistFailedStep_WhenDecisionFails()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();
        var events = new List<AgentRunEvent>();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("fail-decision"));
        _stepDecisionParser.Parse("fail-decision")
            .Returns(StepDecision.Fail("upstream_unavailable", "The upstream system is unavailable."));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        var failed = Assert.IsType<AgentLoopFailed>(result);
        Assert.Equal("failed", failed.StepType);
        Assert.Equal("The upstream system is unavailable.", failed.ErrorMessage);
        Assert.True(ModelFailurePayloadSerializer.TryParse(failed.ErrorPayload, out var payload));
        Assert.NotNull(payload);
        Assert.Equal("upstream_unavailable", payload!.ErrorCode);

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepNo == 4 &&
                step.StepType == "failed" &&
                step.Status == "Failed" &&
                step.ErrorPayload != null &&
                step.ErrorPayload.Contains("\"ErrorCode\":\"upstream_unavailable\"")),
            Arg.Any<CancellationToken>());
        Assert.Collection(events,
            evt => AssertEvent(evt, "step", 3, "model_call", "Completed", "fail-decision"),
            evt =>
            {
                Assert.Equal("step", evt.EventType);
                Assert.Equal(4, evt.Data.StepNo);
                Assert.Equal("failed", evt.Data.StepType);
                Assert.Equal("Failed", evt.Data.Status);
                Assert.Contains("\"ErrorCode\":\"upstream_unavailable\"", evt.Data.Error);
            });
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAccumulateModelCostAcrossTurns()
    {
        var context = CreateLoopContext();
        var resolvedDefinition = CreateResolvedDefinition();

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("tool-decision", Cost: 0.12m),
                new GenerateTextResult("final-answer", Cost: 0.08m));

        _stepDecisionParser.Parse("tool-decision")
            .Returns(StepDecision.ToolCall("weather", "{\"city\":\"Shanghai\"}"));
        _stepDecisionParser.Parse("final-answer")
            .Returns(StepDecision.Respond("Hello from tool"));

        _toolExecutor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "sunny"));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var completed = Assert.IsType<AgentLoopCompleted>(result);
        Assert.Equal(0.20m, completed.TotalCostDelta);

        var modelSteps = _agentStepRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentStepRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<AgentStep>()
            .Where(step => step.StepType == "model_call")
            .ToList();
        Assert.Equal(2, modelSteps.Count);
        Assert.True(ModelCallPayloadSerializer.TryParse(modelSteps[0].DecisionPayload, out var firstPayload));
        Assert.NotNull(firstPayload);
        Assert.Equal("gpt-4o", firstPayload!.Model);
        Assert.Equal(0.12m, firstPayload.Cost);
        Assert.True(ModelCallPayloadSerializer.TryParse(modelSteps[1].DecisionPayload, out var secondPayload));
        Assert.NotNull(secondPayload);
        Assert.Equal(0.08m, secondPayload!.Cost);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenModelCostExceedsRunMaxCost()
    {
        var resolvedDefinition = CreateResolvedDefinition();
        var run = new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            Status = "Running",
            InputPayload = "hello",
            CurrentStepNo = 2,
            MaxTurns = 5,
            MaxCost = 0.10m
        };
        var context = new AgentLoopContext(run, resolvedDefinition.Version, "hello", 3, 0);

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("final-answer", Cost: 0.12m));

        var result = await AgentRunLoop.ExecuteAsync(
            context,
            resolvedDefinition,
            _modelGateway,
            _stepDecisionParser,
            _toolExecutor,
            _agentStepRepository,
            _toolDefinitionRepository,
            _toolInvocationRepository,
            CancellationToken.None);

        var failed = Assert.IsType<AgentLoopFailed>(result);
        Assert.Equal("model_call", failed.StepType);
        Assert.Equal(0.12m, failed.TotalCostDelta);
        Assert.Contains("exceeded the configured maximum 0.1", failed.ErrorMessage);
        _stepDecisionParser.DidNotReceive().Parse(Arg.Any<string>());
    }

    private static void AssertEvent(AgentRunEvent evt, string eventType, int stepNo, string stepType, string status, string? output = null)
    {
        Assert.Equal(eventType, evt.EventType);
        Assert.Equal(stepNo, evt.Data.StepNo);
        Assert.Equal(stepType, evt.Data.StepType);
        Assert.Equal(status, evt.Data.Status);
        Assert.Equal(output, evt.Data.Output);
    }

    private static AgentLoopContext CreateLoopContext()
    {
        var run = new AgentRun
        {
            RunId = "run-001",
            AgentCode = "writer",
            Status = "Running",
            InputPayload = "hello",
            CurrentStepNo = 2,
            MaxTurns = 5
        };

        var version = CreateResolvedDefinition().Version;
        return new AgentLoopContext(run, version, "hello", 3, 0);
    }

    private static ResolvedAgentDefinition CreateResolvedDefinition(
        string? outputSchema = null,
        string? allowedHandoffs = "[]",
        string? routingPolicy = null,
        string? contextPolicy = null,
        string? deniedTools = "[]")
    {
        var now = DateTime.UtcNow;
        return new ResolvedAgentDefinition(
            new AgentDefinition
            {
                Id = "def-1",
                Code = "writer",
                Name = "Writer",
                Enabled = true,
                CurrentVersion = 1,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            },
            new AgentDefinitionVersion
            {
                Id = "ver-1",
                AgentDefinitionId = "def-1",
                Version = 1,
                Status = "Published",
                Name = "Writer v1",
                DefaultModel = "gpt-4o",
                SystemPromptTemplate = "You are a writer.",
                AllowedTools = "[\"weather\"]",
                DeniedTools = deniedTools,
                RoutingPolicy = routingPolicy,
                ContextPolicy = contextPolicy,
                AllowedHandoffs = allowedHandoffs,
                OutputSchema = outputSchema,
                MaxTurns = 5,
                MaxCost = 10m,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            });
    }
}
