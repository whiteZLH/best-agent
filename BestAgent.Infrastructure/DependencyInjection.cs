using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Infrastructure.Model;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using BestAgent.Infrastructure.Persistence.Seeding;
using BestAgent.Infrastructure.Runtime;
using BestAgent.Infrastructure.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BestAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=best_agent;Username=postgres;Password=postgres";
        var openAiOptions = new OpenAiOptions
        {
            BaseUrl = configuration["OpenAI:BaseUrl"] ?? string.Empty,
            ApiKey = configuration["OpenAI:ApiKey"] ?? string.Empty,
            Model = configuration["OpenAI:Model"] ?? string.Empty,
            TimeoutSeconds = int.TryParse(configuration["OpenAI:TimeoutSeconds"], out var timeoutSeconds)
                ? timeoutSeconds
                : 60
        };

        services.AddDbContext<BestAgentDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(openAiOptions);
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<OpenAiOptions>();
            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 60)
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                httpClient.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
            }

            return httpClient;
        });
        services.AddSingleton<IModelGateway, OpenAiCompatibleModelGateway>();
        services.AddHttpClient("ToolWebhook");
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<IToolHandlerRegistry>(sp => sp.GetRequiredService<ToolRegistry>());
        services.AddScoped<IToolResolver, ToolResolver>();
        services.AddScoped<IHttpToolInvoker, HttpToolInvoker>();
        services.AddScoped<IToolExecutor, ToolExecutor>();
        services.AddScoped<IAgentDefinitionRepository, AgentDefinitionRepository>();
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAgentStepRepository, AgentStepRepository>();
        services.AddScoped<IAgentApprovalRepository, AgentApprovalRepository>();
        services.AddScoped<IToolDefinitionRepository, ToolDefinitionRepository>();
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddHostedService<AgentRunWorker>();

        return services;
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }
}
