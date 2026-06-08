using AutoMapper;
using BestAgent.Api.Infrastructure;
using BestAgent.Api.Mappings;
using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Knowledge;
using BestAgent.Domain.Tools;
using BestAgent.Infrastructure;
using BestAgent.Infrastructure.Observability;
using BestAgent.Infrastructure.Persistence.Seeding;
using BestAgent.Infrastructure.Runtime;
using BestAgent.Infrastructure.Tools;
using Microsoft.AspNetCore.Authentication;
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
        services.AddSingleton(new BestAgentAuthenticationOptions());
        services
            .AddAuthentication(BestAgentAuthenticationOptions.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BestAgentAuthenticationHandler>(
                BestAgentAuthenticationOptions.SchemeName,
                _ => { });
        services.AddAuthorization();
        services.AddSingleton(new WebhookSecurityOptions
        {
            RequireSignature = false,
            ToolCallbackSecret = string.Empty,
            ApprovalCallbackSecret = string.Empty,
            ApprovalCallbackSecrets = Array.Empty<string>(),
            SignatureHeaderName = "X-BestAgent-Signature"
        });
        services.AddSingleton<IWebhookRequestAuthorizer, HmacWebhookRequestAuthorizer>();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddAutoMapper(
            _ => { },
            typeof(ApiMappingProfile).Assembly,
            typeof(CreateAgentRunMappingProfile).Assembly);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IProblemDetailsService>());
        Assert.IsType<GlobalExceptionHandler>(provider.GetRequiredService<IExceptionHandler>());
        Assert.NotNull(provider.GetRequiredService<IAuthenticationService>());
        Assert.NotNull(provider.GetRequiredService<IAuthenticationSchemeProvider>());
        Assert.IsType<StepDecisionParser>(provider.GetRequiredService<IStepDecisionParser>());
        Assert.IsType<AgentRunChannel>(provider.GetRequiredService<IAgentRunChannel>());
        Assert.IsType<AgentRunEventBus>(provider.GetRequiredService<IAgentRunEventBus>());
        Assert.IsType<AgentMetrics>(provider.GetRequiredService<IAgentMetrics>());
        Assert.NotNull(provider.GetRequiredService<ApprovalPolicyOptions>());
        Assert.IsType<HmacWebhookRequestAuthorizer>(provider.GetRequiredService<IWebhookRequestAuthorizer>());
        Assert.IsType<RuntimeContextComposer>(provider.GetRequiredService<IRuntimeContextComposer>());
        Assert.IsType<RuntimeMemoryWriter>(provider.GetRequiredService<IRuntimeMemoryWriter>());
        Assert.NotNull(provider.GetRequiredService<IMapper>());
        Assert.NotNull(provider.GetRequiredService<IAgentApprovalRepository>());
        Assert.NotNull(provider.GetRequiredService<IIdempotencyRecordRepository>());
        Assert.NotNull(provider.GetRequiredService<IToolInvocationRepository>());
        Assert.NotNull(provider.GetRequiredService<IKnowledgeDocumentRepository>());
        Assert.NotNull(provider.GetRequiredService<IKnowledgeChunkRepository>());
        Assert.NotNull(provider.GetRequiredService<ISessionMemoryRepository>());
        Assert.NotNull(provider.GetRequiredService<IUserMemoryRepository>());
        Assert.NotNull(provider.GetRequiredService<ISummaryMemoryRepository>());
        Assert.NotNull(provider.GetRequiredService<IToolResolver>());
        Assert.IsType<JsonSchemaToolInputValidator>(provider.GetRequiredService<IToolInputValidator>());
        Assert.IsType<JsonSchemaToolOutputValidator>(provider.GetRequiredService<IToolOutputValidator>());
        Assert.IsType<EventBusRunOutboxEventPublisher>(provider.GetRequiredService<IRunOutboxEventPublisher>());

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, service => service.GetType() == typeof(DatabaseInitializationHostedService));
        Assert.Contains(hostedServices, service => service.GetType() == typeof(AgentRunWorker));
        Assert.Contains(hostedServices, service => service.GetType() == typeof(ApprovalTimeoutDispatcher));
        Assert.Contains(hostedServices, service => service.GetType() == typeof(RunOutboxEventDispatcher));
    }

    [Fact]
    public void AddApplication_ShouldNormalizeApprovalPolicyOptions()
    {
        var services = new ServiceCollection();

        services.AddApplication(new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = ["InternalWrite", "internal_write"],
            RoleRequiredSideEffectLevels = ["Destructive"],
            AllowedApproverRoles = [" Admin ", "admin", " reviewer "],
            ParameterApprovalRules =
            [
                new ApprovalParameterRule
                {
                    ToolName = " weather ",
                    InputPath = " city ",
                    ExpectedValue = " Shanghai ",
                    OverrideSideEffectLevel = "ExternalWrite"
                }
            ]
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ApprovalPolicyOptions>();

        Assert.Equal(["internal_write"], options.ApprovalRequiredSideEffectLevels);
        Assert.Equal(["destructive"], options.RoleRequiredSideEffectLevels);
        Assert.Equal(["Admin", "reviewer"], options.AllowedApproverRoles);
        var rule = Assert.Single(options.ParameterApprovalRules);
        Assert.Equal("weather", rule.ToolName);
        Assert.Equal("city", rule.InputPath);
        Assert.Equal("Shanghai", rule.ExpectedValue);
        Assert.Equal("external_write", rule.OverrideSideEffectLevel);
    }

    [Fact]
    public void AddApplication_ShouldThrow_WhenApprovalPolicyOverrideSideEffectLevelIsInvalid()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddApplication(new ApprovalPolicyOptions
        {
            ParameterApprovalRules =
            [
                new ApprovalParameterRule
                {
                    ToolName = "weather",
                    InputPath = "city",
                    OverrideSideEffectLevel = "mutating"
                }
            ]
        }));

        Assert.Equal("ParameterApprovalRules[0].OverrideSideEffectLevel must be one of: read_only, internal_write, external_write, destructive.", exception.Message);
    }
}
