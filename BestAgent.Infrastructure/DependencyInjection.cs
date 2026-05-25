using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using BestAgent.Infrastructure.Persistence.Seeding;
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

        services.AddDbContext<BestAgentDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IAgentDefinitionRepository, AgentDefinitionRepository>();
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAgentStepRepository, AgentStepRepository>();
        services.AddHostedService<DatabaseInitializationHostedService>();

        return services;
    }
}
