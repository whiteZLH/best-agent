using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.ResumeAgentRun;

public class ResumeAgentRunCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IMediator _mediator;

    public ResumeAgentRunCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentRunChannel);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldEnqueueResumeMessage()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingTool",
            CurrentWaitToken = "token-abc",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "original input",
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);

        var result = await _mediator.Send(new ResumeAgentRunCommand("run-1", "token-abc", """{"result":"done"}"""));

        Assert.Equal("Running", result.Status);
        Assert.Equal("run-1", result.RunId);
        Assert.Equal("writer", result.AgentCode);
        Assert.Null(result.Output);

        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Running" && r.StatusVersion == 3),
            Arg.Any<CancellationToken>());

        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ResumeAgentRunMessage>(rm =>
                rm.RunId == "run-1" &&
                rm.WaitToken == "token-abc" &&
                rm.ToolResult == """{"result":"done"}"""),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongWaitToken_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingTool",
            CurrentWaitToken = "correct-token",
            StatusVersion = 2,
            Creator = "system", CreatorName = "system",
            LastModifier = "system", LastModifierName = "system",
            CreateTime = now, LastModifyTime = now
        };

        _agentRunRepo.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>()).Returns(run);

        var ex = await Assert.ThrowsAsync<Application.Exceptions.ConflictException>(() =>
            _mediator.Send(new ResumeAgentRunCommand("run-1", "wrong-token", "result")));

        Assert.Contains("Wait token mismatch", ex.Message);
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
            _mediator.Send(new ResumeAgentRunCommand("run-1", "any-token", "result")));

        Assert.Contains("expected 'WaitingTool'", ex.Message);
    }
}
