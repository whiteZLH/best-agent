using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CompleteToolInvocation;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.CompleteToolInvocation;

public class CompleteToolInvocationCommandHandlerTests
{
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IIdempotencyRecordRepository _idempotencyRecordRepo = Substitute.For<IIdempotencyRecordRepository>();
    private readonly IToolInvocationRepository _toolInvocationRepo = Substitute.For<IToolInvocationRepository>();
    private readonly IMediator _mediator;

    public CompleteToolInvocationCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_idempotencyRecordRepo);
        services.AddSingleton(_toolInvocationRepo);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldValidatePendingInvocation_AndEnqueueResumeMessage()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingRun(now);
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
        var invocation = CreatePendingInvocation(run.RunId, pendingStep.StepId, now);

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _toolInvocationRepo.GetByInvocationIdAsync(invocation.InvocationId, Arg.Any<CancellationToken>()).Returns(invocation);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(
            new CompleteToolInvocationCommand(run.RunId, invocation.InvocationId, "wait-1", """{"done":true}"""));

        Assert.Equal("Running", result.Status);
        Assert.Equal(run.RunId, result.RunId);
        Assert.Equal("writer", result.AgentCode);

        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(updated =>
                updated.RunId == run.RunId &&
                updated.Status == "Running" &&
                updated.CurrentWaitToken == string.Empty &&
                updated.StatusVersion == 3),
            Arg.Any<CancellationToken>());
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ResumeAgentRunMessage>(message =>
                message.RunId == run.RunId &&
                message.WaitToken == "wait-1" &&
                message.ToolResult == """{"done":true}""" &&
                message.InvocationId == invocation.InvocationId),
            Arg.Any<CancellationToken>());
        await _toolInvocationRepo.DidNotReceive().UpdateAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithIdempotencyKey_ShouldStoreRecordAfterEnqueue()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingRun(now);
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
        var invocation = CreatePendingInvocation(run.RunId, pendingStep.StepId, now);

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _toolInvocationRepo.GetByInvocationIdAsync(invocation.InvocationId, Arg.Any<CancellationToken>()).Returns(invocation);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _agentStepRepo.GetByStepIdAsync(pendingStep.StepId, Arg.Any<CancellationToken>()).Returns(pendingStep);

        var result = await _mediator.Send(
            new CompleteToolInvocationCommand(run.RunId, invocation.InvocationId, "wait-1", """{"done":true}""", " callback-1 "));

        Assert.Equal("Running", result.Status);
        await _idempotencyRecordRepo.Received(1).AddAsync(
            Arg.Is<IdempotencyRecord>(record =>
                record.ScopeType == "tool_complete" &&
                record.ScopeKey.Length == 64 &&
                record.RequestHash.Length == 64 &&
                record.TargetId == invocation.InvocationId &&
                record.Status == "completed" &&
                record.ExtraPayload != null &&
                record.ExtraPayload.Contains(run.RunId)),
            Arg.Any<CancellationToken>());
        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<ResumeAgentRunMessage>(message => message.RunId == run.RunId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReplayedIdempotencyKey_ShouldReturnStoredResultWithoutEnqueue()
    {
        var storedResult = new CompleteToolInvocationResult("run-1", "writer", "original input", null, "Running");
        var record = CreateIdempotencyRecord(
            "run-1",
            "invocation-1",
            "callback-1",
            "wait-1",
            "result",
            storedResult);

        _idempotencyRecordRepo.GetByScopeAsync("tool_complete", record.ScopeKey, Arg.Any<CancellationToken>())
            .Returns(record);

        var result = await _mediator.Send(
            new CompleteToolInvocationCommand("run-1", "invocation-1", "wait-1", "result", "callback-1"));

        Assert.Equal("Running", result.Status);
        Assert.Equal("run-1", result.RunId);
        await _agentRunRepo.DidNotReceive().GetByRunIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<ResumeAgentRunMessage>(), Arg.Any<CancellationToken>());
        await _idempotencyRecordRepo.DidNotReceive().AddAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReplayedIdempotencyKeyWithDifferentPayload_ShouldThrowConflict()
    {
        var storedResult = new CompleteToolInvocationResult("run-1", "writer", "original input", null, "Running");
        var record = CreateIdempotencyRecord(
            "run-1",
            "invocation-1",
            "callback-1",
            "wait-1",
            "original-result",
            storedResult);

        _idempotencyRecordRepo.GetByScopeAsync("tool_complete", record.ScopeKey, Arg.Any<CancellationToken>())
            .Returns(record);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(new CompleteToolInvocationCommand("run-1", "invocation-1", "wait-1", "different-result", "callback-1")));

        Assert.Contains("already used", ex.Message);
        await _agentRunRepo.DidNotReceive().GetByRunIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<ResumeAgentRunMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongInvocationId_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingRun(now);
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

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(new CompleteToolInvocationCommand(run.RunId, "wrong-step", "wait-1", "result")));

        Assert.Contains("not the current pending invocation", ex.Message);
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<ResumeAgentRunMessage>(), Arg.Any<CancellationToken>());
        await _toolInvocationRepo.DidNotReceive().UpdateAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongWaitToken_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingRun(now);

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(new CompleteToolInvocationCommand(run.RunId, "step-1", "wrong", "result")));

        Assert.Contains("Wait token mismatch", ex.Message);
        await _agentStepRepo.DidNotReceive().GetLastByRunIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvocationForDifferentStep_ShouldThrowConflict()
    {
        var now = DateTime.UtcNow;
        var run = CreateWaitingRun(now);
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
        var invocation = CreatePendingInvocation(run.RunId, "different-step", now);

        _agentRunRepo.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        _toolInvocationRepo.GetByInvocationIdAsync(invocation.InvocationId, Arg.Any<CancellationToken>()).Returns(invocation);
        _agentStepRepo.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(pendingStep);
        _agentStepRepo.GetByStepIdAsync("different-step", Arg.Any<CancellationToken>()).Returns((AgentStep?)null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _mediator.Send(new CompleteToolInvocationCommand(run.RunId, invocation.InvocationId, "wait-1", "result")));

        Assert.Contains("not the current pending invocation", ex.Message);
        await _agentRunRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<ResumeAgentRunMessage>(), Arg.Any<CancellationToken>());
    }

    private static AgentRun CreateWaitingRun(DateTime now)
    {
        return new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingTool",
            CurrentWaitToken = "wait-1",
            CurrentStepNo = 4,
            StatusVersion = 2,
            InputPayload = "original input",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }

    private static IdempotencyRecord CreateIdempotencyRecord(
        string runId,
        string invocationId,
        string idempotencyKey,
        string waitToken,
        string toolResult,
        CompleteToolInvocationResult result)
    {
        var now = DateTime.UtcNow;
        return new IdempotencyRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            ScopeType = "tool_complete",
            ScopeKey = Hash($"{runId}:{invocationId}:{idempotencyKey}"),
            RequestHash = Hash(System.Text.Json.JsonSerializer.Serialize(new { waitToken, toolResult })),
            TargetId = invocationId,
            Status = "completed",
            ExtraPayload = System.Text.Json.JsonSerializer.Serialize(result),
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }

    private static string Hash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ToolInvocation CreatePendingInvocation(string runId, string stepId, DateTime now)
    {
        return new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = runId,
            StepId = stepId,
            ToolName = "writer",
            Mode = "async",
            Status = "Pending",
            InputPayload = "input",
            IdempotencyKey = "invocation-1",
            CallbackToken = "wait-1",
            StartedAt = now,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }
}
