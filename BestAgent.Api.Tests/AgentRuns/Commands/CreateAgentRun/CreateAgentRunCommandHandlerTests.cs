using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Commands.CreateAgentRun;

public class CreateAgentRunCommandHandlerTests
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
    private readonly IAgentRunRepository _agentRunRepo = Substitute.For<IAgentRunRepository>();
    private readonly IAgentStepRepository _agentStepRepo = Substitute.For<IAgentStepRepository>();
    private readonly IAgentRunChannel _agentRunChannel = Substitute.For<IAgentRunChannel>();
    private readonly IAgentMetrics _agentMetrics = Substitute.For<IAgentMetrics>();
    private readonly IMediator _mediator;

    public CreateAgentRunCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentDefinitionRepo);
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
        services.AddSingleton(_agentMetrics);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldCreateRunAndEnqueue()
    {
        var now = DateTime.UtcNow;
        _agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(new ResolvedAgentDefinition(
                new AgentDefinition
                {
                    Id = "def-1", Code = "writer", Name = "Writer", Enabled = true, CurrentVersion = 1,
                    Creator = "system", CreatorName = "system", LastModifier = "system", LastModifierName = "system",
                    CreateTime = now, LastModifyTime = now
                },
                new AgentDefinitionVersion
                {
                    Id = "ver-1", AgentDefinitionId = "def-1", Version = 1, Status = "Published",
                    Name = "Writer v1", DefaultModel = "gpt-4o", SystemPromptTemplate = "You are a writer.",
                    MaxTurns = 5, MaxCost = 10m,
                    Creator = "system", CreatorName = "system", LastModifier = "system", LastModifierName = "system",
                    CreateTime = now, LastModifyTime = now
                }));

        var result = await _mediator.Send(new CreateAgentRunCommand("writer", "Say hi", "idem-1", "tenant-1", "user-1", "session-1", true, 3));

        Assert.Equal("writer", result.AgentCode);
        Assert.Equal("Say hi", result.Input);
        Assert.Null(result.Output);
        Assert.Equal("Running", result.Status);
        Assert.NotNull(result.RunId);

        await _agentRunRepo.Received(1).AddAsync(
            Arg.Is<AgentRun>(r =>
                r.AgentCode == "writer" &&
                r.AgentDefinitionVersionId == "ver-1" &&
                r.Status == "Running" &&
                r.InputPayload == "Say hi" &&
                r.IdempotencyKey == "idem-1" &&
                r.TenantId == "tenant-1" &&
                r.UserId == "user-1" &&
                r.SessionId == "session-1" &&
                r.RootRunId == r.RunId &&
                r.ParentRunId == string.Empty &&
                r.DelegatedByRunId == string.Empty &&
                r.DelegatedByAgent == string.Empty &&
                r.MaxTurns == 3 &&
                r.MaxCost == 10m),
            Arg.Any<CancellationToken>());

        await _agentStepRepo.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());

        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<AgentRunMessage>(m => m is CreateAgentRunMessage && m.RunId == result.RunId),
            Arg.Any<CancellationToken>());
        _agentMetrics.Received(1).RecordRunCreated("writer", false);
    }

    [Fact]
    public async Task Handle_WithExistingIdempotencyKey_ShouldReturnExistingRun_WithoutCreatingOrEnqueueing()
    {
        var now = DateTime.UtcNow;
        var existingRun = new AgentRun
        {
            RunId = "run-existing",
            AgentCode = "writer",
            AgentDefinitionVersionId = "ver-1",
            Status = "WaitingTool",
            InputPayload = "Say hi",
            OutputPayload = null,
            CurrentWaitToken = "wait-1",
            IdempotencyKey = "idem-1",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        _agentRunRepo.GetByIdempotencyKeyAsync("idem-1", Arg.Any<CancellationToken>()).Returns(existingRun);

        var result = await _mediator.Send(new CreateAgentRunCommand("writer", "Say hi", " idem-1 "));

        Assert.Equal("run-existing", result.RunId);
        Assert.Equal("writer", result.AgentCode);
        Assert.Equal("Say hi", result.Input);
        Assert.Equal("WaitingTool", result.Status);
        Assert.Equal("wait-1", result.WaitToken);
        await _agentDefinitionRepo.DidNotReceive().GetEnabledByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _agentRunRepo.DidNotReceive().AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentStepRepo.DidNotReceive().AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<AgentRunMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenDefinitionMissing()
    {
        _agentDefinitionRepo.GetEnabledByCodeAsync("missing", Arg.Any<CancellationToken>())
            .Returns((ResolvedAgentDefinition?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() =>
            _mediator.Send(new CreateAgentRunCommand("missing", "input")));
    }

    [Fact]
    public async Task Handle_WhenMaxRoundsExceedsDefinitionLimit_ShouldClampToDefinitionMaxTurns()
    {
        var now = DateTime.UtcNow;
        _agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(new ResolvedAgentDefinition(
                new AgentDefinition
                {
                    Id = "def-1", Code = "writer", Name = "Writer", Enabled = true, CurrentVersion = 1,
                    Creator = "system", CreatorName = "system", LastModifier = "system", LastModifierName = "system",
                    CreateTime = now, LastModifyTime = now
                },
                new AgentDefinitionVersion
                {
                    Id = "ver-1", AgentDefinitionId = "def-1", Version = 1, Status = "Published",
                    Name = "Writer v1", DefaultModel = "gpt-4o", SystemPromptTemplate = "You are a writer.",
                    MaxTurns = 5, MaxCost = 10m,
                    Creator = "system", CreatorName = "system", LastModifier = "system", LastModifierName = "system",
                    CreateTime = now, LastModifyTime = now
                }));

        await _mediator.Send(new CreateAgentRunCommand("writer", "Say hi", null, null, null, null, null, 8));

        await _agentRunRepo.Received(1).AddAsync(
            Arg.Is<AgentRun>(r => r.MaxTurns == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenMaxRoundsIsNotPositive_ShouldThrowInvalidOperation()
    {
        var now = DateTime.UtcNow;
        _agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
            .Returns(new ResolvedAgentDefinition(
                new AgentDefinition
                {
                    Id = "def-1", Code = "writer", Name = "Writer", Enabled = true, CurrentVersion = 1,
                    Creator = "system", CreatorName = "system", LastModifier = "system", LastModifierName = "system",
                    CreateTime = now, LastModifyTime = now
                },
                new AgentDefinitionVersion
                {
                    Id = "ver-1", AgentDefinitionId = "def-1", Version = 1, Status = "Published",
                    Name = "Writer v1", DefaultModel = "gpt-4o", SystemPromptTemplate = "You are a writer.",
                    MaxTurns = 5, MaxCost = 10m,
                    Creator = "system", CreatorName = "system", LastModifier = "system", LastModifierName = "system",
                    CreateTime = now, LastModifyTime = now
                }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _mediator.Send(new CreateAgentRunCommand("writer", "Say hi", null, null, null, null, null, 0)));

        Assert.Equal("Options.MaxRounds must be greater than zero.", ex.Message);
        await _agentRunRepo.DidNotReceive().AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await _agentRunChannel.DidNotReceive().EnqueueAsync(Arg.Any<AgentRunMessage>(), Arg.Any<CancellationToken>());
    }
}
