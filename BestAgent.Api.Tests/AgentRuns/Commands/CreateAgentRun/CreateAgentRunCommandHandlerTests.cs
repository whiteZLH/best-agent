using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.Models;
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
    private readonly IModelGateway _modelGateway = Substitute.For<IModelGateway>();
    private readonly IMediator _mediator;

    public CreateAgentRunCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentDefinitionRepo);
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_modelGateway);
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ShouldCompleteRun_WhenModelCallSucceeds()
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

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateTextResult("Hello from model"));

        var result = await _mediator.Send(new CreateAgentRunCommand("writer", "Say hi"));

        // 验证 handler 确实被调用：GetEnabledByCodeAsync 是 Handle 方法第一行
        await _agentDefinitionRepo.Received(1).GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>());

        Assert.Equal("writer", result.AgentCode);
        Assert.Equal("Say hi", result.Input);
        Assert.Equal("Hello from model", result.Output);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.RunId);

        await _agentRunRepo.Received(1).AddAsync(
            Arg.Is<AgentRun>(r =>
                r.AgentCode == "writer" &&
                r.AgentDefinitionVersionId == "ver-1" &&
                r.Status == "Running" &&
                r.InputPayload == "Say hi" &&
                r.RunId == r.RootRunId &&
                r.RunId == r.IdempotencyKey &&
                r.MaxTurns == 5 &&
                r.MaxCost == 10m),
            Arg.Any<CancellationToken>());
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Completed" && r.OutputPayload == "Hello from model"),
            Arg.Any<CancellationToken>());
        await _agentStepRepo.Received(4).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
    }
}
