using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.Models;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Model;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace BestAgent.Api.Tests.AgentRuns.Commands.CreateAgentRun;

[Trait("Category", "Integration")]
public class CreateAgentRunCommandHandlerIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CreateAgentRunCommandHandlerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Handle_ShouldCallRealModelApi()
    {
        var openAiOptions = new OpenAiOptions
        {
            BaseUrl = "https://api.liangrekui.com/v1/",
            ApiKey = "",
            Model = "gpt-5.4"
        };

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(openAiOptions.BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var modelGateway = new OpenAiCompatibleModelGateway(httpClient, openAiOptions);

        var agentDefinitionRepo = Substitute.For<IAgentDefinitionRepository>();
        var agentRunRepo = Substitute.For<IAgentRunRepository>();
        var agentStepRepo = Substitute.For<IAgentStepRepository>();

        var now = DateTime.UtcNow;
        agentDefinitionRepo.GetEnabledByCodeAsync("writer", Arg.Any<CancellationToken>())
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
                    Name = "Writer v1", DefaultModel = "gpt-5.4",
                    SystemPromptTemplate = "You are a helpful assistant. Reply in one short sentence.",
                    MaxTurns = 5, MaxCost = 10m,
                    Creator = "system", CreatorName = "system", LastModifier = "system", LastModifierName = "system",
                    CreateTime = now, LastModifyTime = now
                }));

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IModelGateway>(modelGateway);
        services.AddSingleton(agentDefinitionRepo);
        services.AddSingleton(agentRunRepo);
        services.AddSingleton(agentStepRepo);

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var result = await mediator.Send(new CreateAgentRunCommand("writer", "What is 1+1?"));

        _output.WriteLine($"RunId: {result.RunId}");
        _output.WriteLine($"Output: {result.Output}");

        Assert.Equal("writer", result.AgentCode);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.Output);
        Assert.NotEmpty(result.Output);

        await agentRunRepo.Received(1).AddAsync(Arg.Any<AgentRun>(), Arg.Any<CancellationToken>());
        await agentRunRepo.Received(1).UpdateAsync(
            Arg.Is<AgentRun>(r => r.Status == "Completed"),
            Arg.Any<CancellationToken>());
        await agentStepRepo.Received(4).AddAsync(Arg.Any<AgentStep>(), Arg.Any<CancellationToken>());
    }
}
