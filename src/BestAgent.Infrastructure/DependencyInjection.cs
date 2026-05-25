using BestAgent.Application.Abstractions;
using BestAgent.Domain.Agents;
using BestAgent.Infrastructure.Model;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Repositories;
using BestAgent.Infrastructure.Seeding;
using BestAgent.Infrastructure.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BestAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "Postgres";

        services
            .AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "OpenAI:BaseUrl is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "OpenAI:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model), "OpenAI:Model is required.")
            .Validate(options => options.TimeoutSeconds > 0, "OpenAI:TimeoutSeconds must be positive.")
            .ValidateOnStart();

        services.AddDbContext<BestAgentDbContext>(options =>
        {
            if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                var databaseName = configuration["Persistence:DatabaseName"] ?? "best-agent-tests";
                options.UseInMemoryDatabase(databaseName);
                return;
            }

            var connectionString = configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<BestAgentDbContext>());
        services.AddScoped<IAgentDefinitionRepository, AgentDefinitionRepository>();
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAgentStepRepository, AgentStepRepository>();
        services.AddScoped<IAgentMessageRepository, AgentMessageRepository>();
        services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();
        services.AddScoped<IOutboxEventRepository, OutboxEventRepository>();
        services.AddScoped<IToolInvocationRepository, ToolInvocationRepository>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolExecutor, ToolExecutor>();
        services.AddHttpClient<IModelGateway, OpenAiCompatibleModelGateway>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }

    public static async Task EnsureDatabaseInitializedAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BestAgentDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAiOptions>>().Value;

        if (dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        if (!await dbContext.AgentDefinitions.AnyAsync(cancellationToken))
        {
            dbContext.AgentDefinitions.Add(AgentDefinitionSeeder.CreateDefault(options.Model, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
