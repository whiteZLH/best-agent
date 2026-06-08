using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CancelAgentRun;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.CancelAgentRun;

public class CancelAgentRunCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IRunOutboxEventRepository _runOutboxEventRepo = Substitute.For<IRunOutboxEventRepository>();
    private readonly IAgentRunEventBus _eventBus = Substitute.For<IAgentRunEventBus>();
    private readonly IAgentMetrics _agentMetrics = Substitute.For<IAgentMetrics>();
    private readonly IMediator _mediator;

    public CancelAgentRunCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_runOutboxEventRepo);
        services.AddSingleton(_eventBus);
        services.AddSingleton(_agentMetrics);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldCancelWaitingRun_CancelPendingStep_WriteOutbox_AndPublishEvent()
    {
        var now = DateTime.UtcNow;
        var run = CreateRun("WaitingTool", now) with
        {
            CurrentWaitToken = "wait-1",
            CurrentStepNo = 4,
            StatusVersion = 2
        };
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "input",
            null,
            null,
            now,
            now);

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _runOutboxEventRepo.GetNextSeqNoAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(8L);

        var result = await _mediator.Send(new CancelAgentRunCommand(run.RunId, " user stopped "));

        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(run.RunId, result.RunId);
        Assert.Equal("user stopped", result.Reason);
        Assert.Null(result.WaitToken);

        await _agentStepRepo.Received(1).UpdateAsync(
            Arg.Is<AgentStep>(step =>
                step.StepId == pendingStep.StepId &&
                step.Status == "Cancelled" &&
                step.ErrorPayload == "user stopped"),
            Arg.Any<CancellationToken>());
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(updated =>
                updated.RunId == run.RunId &&
                updated.Status == "Cancelled" &&
                updated.CurrentWaitToken == string.Empty &&
                updated.InterruptReason == "user stopped" &&
                updated.StatusVersion == 3 &&
                updated.EndedAt.HasValue),
            Arg.Any<CancellationToken>());
        await _runOutboxEventRepo.Received(1).AddAsync(
            Arg.Is<RunOutboxEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.SeqNo == 8 &&
                evt.EventType == "cancelled" &&
                evt.RunStatus == "Cancelled" &&
                evt.PublishStatus == "pending" &&
                evt.Payload != null &&
                evt.Payload.Contains("user stopped")),
            Arg.Any<CancellationToken>());
        _eventBus.Received(1).Publish(
            Arg.Is<AgentRunEvent>(evt =>
                evt.RunId == run.RunId &&
                evt.EventType == "cancelled" &&
                evt.Data.Status == "Cancelled" &&
                evt.Data.Error == "user stopped"));
        _agentMetrics.Received(1).RecordRunCompleted("writer", "Cancelled", 0m);
        await _runOutboxEventRepo.Received(1).MarkPublishedAsync(
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CompletedRun_ShouldReturnCurrentState_WithoutUpdating()
    {
        var now = DateTime.UtcNow;
        var run = CreateRun("Completed", now) with
        {
            OutputPayload = "done",
            InterruptReason = string.Empty,
            StatusVersion = 4
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);

        var result = await _mediator.Send(new CancelAgentRunCommand(run.RunId, "too late"));

        Assert.Equal("Completed", result.Status);
        Assert.Equal("done", result.Output);
        await _agentStepRepo.DidNotReceive().GetLastByRunIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _runOutboxEventRepo.DidNotReceive().AddAsync(Arg.Any<RunOutboxEvent>(), Arg.Any<CancellationToken>());
        _eventBus.DidNotReceive().Publish(Arg.Any<AgentRunEvent>());
    }

    [Fact]
    public async Task Handle_MissingRun_ShouldThrowNotFound()
    {
        _agentRunRepo.GetByRunIdAsync("missing", Arg.Any<CancellationToken>()).Returns((AgentRun?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _mediator.Send(new CancelAgentRunCommand("missing", null)));

        Assert.Contains("AgentRun", ex.Message);
    }

    private static AgentRun CreateRun(string status, DateTime now)
    {
        return new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = status,
            InputPayload = "original input",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }
}
