using BestAgent.Domain.AgentDefinitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BestAgent.Infrastructure.Persistence.Seeding;

public class DatabaseInitializationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseInitializationHostedService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BestAgentDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (await repository.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var definitionId = Guid.NewGuid().ToString("N");
        var versionId = Guid.NewGuid().ToString("N");

        var definition = new AgentDefinition
        {
            Id = definitionId,
            Code = "default-agent",
            Name = "Default Agent",
            Description = "Seeded default agent for local development.",
            Enabled = true,
            CurrentVersion = 1,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        var version = new AgentDefinitionVersion
        {
            Id = versionId,
            AgentDefinitionId = definitionId,
            Version = 1,
            Status = AgentDefinitionVersionStatuses.Published,
            Name = "Default Agent v1",
            Description = "Default runtime definition.",
            Instruction = "You are the default agent for local development.",
            SystemPromptTemplate = "You are a helpful agent.",
            DefaultModel = "gpt-4.1-mini",
            AllowedTools = "[]",
            KnowledgeSources = "[]",
            AllowedHandoffs = "[]",
            MaxTurns = 8,
            MaxCost = 0,
            PublishedAt = now,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        await repository.AddAsync(new ResolvedAgentDefinition(definition, version), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
