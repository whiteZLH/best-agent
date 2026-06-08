using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CompleteHumanAgentRun;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.CompleteHumanAgentRun;

public class CompleteHumanAgentRunCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IMediator _mediator;

    public CompleteHumanAgentRunCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldEnqueueHumanCompletionMessage()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingHuman",
            CurrentWaitToken = "human-wait-1",
            CurrentStepNo = 5,
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 5, "human_wait", "Pending", "Need operator review", null, null, now, now) with
        {
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(
                HumanApprovalPayloadSerializer.CreatePending("Need operator review"))
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(
            new CompleteHumanAgentRunCommand(
                run.RunId,
                pendingStep.StepId,
                "human-wait-1",
                "Human supplied answer",
                "Resolved manually",
                false,
                "u-2",
                "Bob",
                "operator"));

        Assert.Equal("Running", result.Status);
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(updated =>
                updated.RunId == run.RunId &&
                updated.Status == "Running" &&
                updated.CurrentWaitToken == string.Empty &&
                updated.StatusVersion == 3),
            Arg.Any<CancellationToken>());
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<CompleteHumanAgentRunMessage>(message =>
                message.RunId == run.RunId &&
                message.StepId == pendingStep.StepId &&
                message.WaitToken == "human-wait-1" &&
                message.HumanResult == "Human supplied answer" &&
                message.Comment == "Resolved manually" &&
                !message.Terminate &&
                message.HumanOperatorId == "u-2" &&
                message.HumanOperatorName == "Bob" &&
                message.HumanOperatorRole == "operator"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWaitTokenDoesNotMatch_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingHuman",
            CurrentWaitToken = "human-wait-1",
            CurrentStepNo = 5,
            StatusVersion = 2,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(new CompleteHumanAgentRunCommand(run.RunId, "step-1", "wrong-token", "result", null, false, null, null, null)));

        Assert.Contains("Wait token mismatch", ex.Message);
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<AgentRunMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutHumanOperatorIdentity_ShouldThrowForbidden()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingHuman",
            CurrentWaitToken = "human-wait-1",
            CurrentStepNo = 5,
            StatusVersion = 2,
            InputPayload = "hello",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 5, "human_wait", "Pending", "Need operator review", null, null, now, now) with
        {
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(
                HumanApprovalPayloadSerializer.CreatePending("Need operator review"))
        };

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _mediator.Send(
                new CompleteHumanAgentRunCommand(
                    run.RunId,
                    pendingStep.StepId,
                    "human-wait-1",
                    "Human supplied answer",
                    "Resolved manually",
                    false,
                    null,
                    null,
                    null)));

        Assert.Contains("requires an authenticated or explicit operator identity", ex.Message);
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<AgentRunMessage>(), Arg.Any<CancellationToken>());
    }
}
