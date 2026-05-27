using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
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
    private readonly IToolExecutor _toolExecutor = Substitute.For<IToolExecutor>();
    private readonly IMediator _mediator;

    public CreateAgentRunCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_agentDefinitionRepo);
        services.AddSingleton(_agentRunRepo);
        services.AddSingleton(_agentStepRepo);
        services.AddSingleton(_modelGateway);
        services.AddSingleton(_toolExecutor);
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
            .Returns(new GenerateTextResult("""{"action":"respond","response":"Hello from model"}"""));

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
        await _toolExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldExecuteTool_ThenCompleteRun()
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
                    AllowedTools = """["echo_context"]""",
                    MaxTurns = 5, MaxCost = 10m,
                    Creator = "system", CreatorName = "system", LastModifier = "system", LastModifierName = "system",
                    CreateTime = now, LastModifyTime = now
                }));

        _modelGateway.GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GenerateTextResult("""{"action":"tool_call","toolName":"echo_context","toolInput":"summarize context"}"""),
                new GenerateTextResult("""{"action":"respond","response":"Final answer after tool"}"""));

        _toolExecutor.ExecuteAsync(
                "echo_context",
                "summarize context",
                Arg.Any<ToolExecutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("echo_context", """{"tool":"ok"}"""));

        var result = await _mediator.Send(new CreateAgentRunCommand("writer", "Say hi"));

        Assert.Equal("writer", result.AgentCode);
        Assert.Equal("Say hi", result.Input);
        Assert.Equal("Final answer after tool", result.Output);
        Assert.Equal("Completed", result.Status);

        await _modelGateway.Received(2).GenerateTextAsync(Arg.Any<GenerateTextRequest>(), Arg.Any<CancellationToken>());
        await _toolExecutor.Received(1).ExecuteAsync(
            "echo_context",
            "summarize context",
            Arg.Is<ToolExecutionContext>(x => x.AgentCode == "writer" && x.UserInput == "Say hi"),
            Arg.Any<CancellationToken>());
        await _agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Completed" && r.OutputPayload == "Final answer after tool"),
            Arg.Any<CancellationToken>());
        await _agentStepRepo.Received(6).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
    }
}
