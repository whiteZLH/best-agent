using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Runtime;

public class AgentRunLoopTests
{
    private readonly IModelGateway _modelGateway = Substitute.For<IModelGateway>();
    private readonly IStepDecisionParser _stepDecisionParser = Substitute.For<IStepDecisionParser>();
    private readonly IToolExecutor _toolExecutor = Substitute.For<IToolExecutor>();
    private readonly IAgentStepRepository _agentStepRepository = Substitute.For<IAgentStepRepository>();

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
            CancellationToken.None,
            events.Add);

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
            CancellationToken.None,
            events.Add);

        var suspended = Assert.IsType<AgentLoopSuspended>(result);
        Assert.Equal("wait-123", suspended.WaitToken);
        Assert.Equal(4, suspended.SuspendedAtStepNo);

        await _agentStepRepository.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentStepRepository.Received(1).AddAsync(
            Arg.Is<AgentStep>(step =>
                step.StepNo == 4 &&
                step.StepType == "tool_call" &&
                step.Status == "Pending" &&
                step.InputPayload == "{\"city\":\"Shanghai\"}" &&
                step.OutputPayload == null),
            Arg.Any<CancellationToken>());

        Assert.Single(events);
        AssertEvent(events[0], "step", 3, "model_call", "Completed", "tool-decision");
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

    private static ResolvedAgentDefinition CreateResolvedDefinition()
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
