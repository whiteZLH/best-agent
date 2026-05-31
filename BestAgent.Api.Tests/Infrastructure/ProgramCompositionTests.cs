using AutoMapper;
using BestAgent.Api.Infrastructure;
using BestAgent.Api.Mappings;
using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure;
using BestAgent.Infrastructure.Persistence.Seeding;
using BestAgent.Infrastructure.Runtime;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class ProgramCompositionTests
{
    [Fact]
    public void ServiceCollection_ShouldRegisterProgramLevelServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=best_agent;Username=postgres;Password=postgres",
                ["OpenAI:BaseUrl"] = "https://example.com/v1/",
                ["OpenAI:ApiKey"] = "test-key",
                ["OpenAI:Model"] = "gpt-4o",
                ["OpenAI:TimeoutSeconds"] = "30"
            })
            .Build();

        services.AddControllers();
        services.AddProblemDetails();
        services.AddSingleton(Substitute.For<IHostEnvironment>());
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddAutoMapper(
            _ => { },
            typeof(ApiMappingProfile).Assembly,
            typeof(CreateAgentRunMappingProfile).Assembly);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IProblemDetailsService>());
        Assert.IsType<GlobalExceptionHandler>(provider.GetRequiredService<IExceptionHandler>());
        Assert.IsType<StepDecisionParser>(provider.GetRequiredService<IStepDecisionParser>());
        Assert.IsType<AgentRunChannel>(provider.GetRequiredService<IAgentRunChannel>());
        Assert.IsType<AgentRunEventBus>(provider.GetRequiredService<IAgentRunEventBus>());
        Assert.NotNull(provider.GetRequiredService<IMapper>());
        Assert.NotNull(provider.GetRequiredService<IAgentApprovalRepository>());
        Assert.NotNull(provider.GetRequiredService<IToolResolver>());

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, service => service.GetType() == typeof(DatabaseInitializationHostedService));
        Assert.Contains(hostedServices, service => service.GetType() == typeof(AgentRunWorker));
    }
}
