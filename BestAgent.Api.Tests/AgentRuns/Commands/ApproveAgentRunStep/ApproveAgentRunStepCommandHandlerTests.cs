using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.ApproveAgentRunStep;

public class ApproveAgentRunStepCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IMediator _mediator;

    public ApproveAgentRunStepCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldEnqueueApproveMessage()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingApproval",
            CurrentWaitToken = "token-abc",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "original input",
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(
            run.RunId,
            4,
            "tool_call",
            "Pending",
            "{\"city\":\"Shanghai\"}",
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(ApprovalPayloadSerializer.CreatePending("weather", "{\"city\":\"Shanghai\"}", "internal_write"))
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId));

        Assert.Equal("Running", result.Status);
        Assert.Equal("run-1", result.RunId);
        Assert.Equal("writer", result.AgentCode);

        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Running" && r.CurrentWaitToken == string.Empty && r.StatusVersion == 3),
            Arg.Any<CancellationToken>());

        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ApproveAgentRunStepMessage>(msg =>
                msg.RunId == "run-1" &&
                msg.StepId == pendingStep.StepId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongStatus_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "Completed",
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);

        var ex = await Assert.ThrowsAsync<Application.Exceptions.ConflictException>(() =>
            _mediator.Send(new ApproveAgentRunStepCommand("run-1", "step-1")));

        Assert.Contains("expected 'WaitingApproval'", ex.Message);
    }

    [Fact]
    public async Task Handle_NonApprovalStep_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingApproval",
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Pending", "input", null, null, now, now);

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var ex = await Assert.ThrowsAsync<Application.Exceptions.ConflictException>(() =>
            _mediator.Send(new ApproveAgentRunStepCommand("run-1", pendingStep.StepId)));

        Assert.Contains("not waiting for approval", ex.Message);
    }
}
