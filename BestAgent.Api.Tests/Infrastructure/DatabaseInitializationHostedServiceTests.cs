using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.Tools;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class DatabaseInitializationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_ShouldEnsureDatabaseAndSeedAgentAndTools_WhenRepositoriesAreEmpty()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(false);
        toolDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(false);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        await agentDefinitionRepository.Received(1).AddAsync(
            Arg.Is<ResolvedAgentDefinition>(definition =>
                definition.Definition.Code == "default-agent" &&
                definition.Version.Name == "Default Agent v1" &&
                definition.Version.DefaultModel == "gpt-4.1-mini"),
            Arg.Any<CancellationToken>());

        await toolDefinitionRepository.Received(2).AddAsync(
            Arg.Any<ToolDefinition>(),
            Arg.Any<CancellationToken>());
        await toolDefinitionRepository.Received(1).AddAsync(
            Arg.Is<ToolDefinition>(tool =>
                tool.ToolName == "echo_context" &&
                tool.SideEffectLevel == "read_only" &&
                tool.Enabled),
            Arg.Any<CancellationToken>());
        await toolDefinitionRepository.Received(1).AddAsync(
            Arg.Is<ToolDefinition>(tool =>
                tool.ToolName == "async_task" &&
                tool.SideEffectLevel == "internal_write" &&
                tool.AsyncSupported),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldSkipSeeding_WhenRepositoriesAlreadyContainData()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        await agentDefinitionRepository.DidNotReceive().AddAsync(Arg.Any<ResolvedAgentDefinition>(), Arg.Any<CancellationToken>());
        await toolDefinitionRepository.DidNotReceive().AddAsync(Arg.Any<ToolDefinition>(), Arg.Any<CancellationToken>());
    }
}
