using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Runtime;
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
    private readonly IMediator _mediator;

    public CreateAgentRunCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentDefinitionRepo);
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_agentRunChannel);
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

        var result = await _mediator.Send(new CreateAgentRunCommand("writer", "Say hi"));

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
                r.MaxTurns == 5 &&
                r.MaxCost == 10m),
            Arg.Any<CancellationToken>());

        await _agentStepRepo.Received(2).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());

        await _agentRunChannel.Received(1).EnqueueAsync(
            Arg.Is<AgentRunMessage>(m => m is CreateAgentRunMessage && m.RunId == result.RunId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenDefinitionMissing()
    {
        _agentDefinitionRepo.GetEnabledByCodeAsync("missing", Arg.Any<CancellationToken>())
            .Returns((ResolvedAgentDefinition?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() =>
            _mediator.Send(new CreateAgentRunCommand("missing", "input")));
    }
}
