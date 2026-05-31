using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.RejectAgentRunStep;

public class RejectAgentRunStepCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IMediator _mediator;

    public RejectAgentRunStepCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldEnqueueRejectMessage()
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
        var pendingStep = AgentRunLoop.CreateStep(run.RunId, 4, "tool_call", "Pending", "input", null, null, now, now) with
        {
            DecisionPayload = ApprovalPayloadSerializer.Serialize(ApprovalPayloadSerializer.CreatePending("weather", "input", "internal_write"))
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);
        _agentStepRepo.GetLastByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(new RejectAgentRunStepCommand("run-1", pendingStep.StepId, "Denied", "u-1", "Alice", "admin"));

        Assert.Equal("Running", result.Status);
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Running" && r.CurrentWaitToken == string.Empty),
            Arg.Any<CancellationToken>());
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<RejectAgentRunStepMessage>(msg =>
                msg.RunId == "run-1" &&
                msg.StepId == pendingStep.StepId &&
                msg.Comment == "Denied" &&
                msg.ApproverId == "u-1" &&
                msg.ApproverName == "Alice" &&
                msg.ApproverRole == "admin"),
            Arg.Any<CancellationToken>());
    }
}
