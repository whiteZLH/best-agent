using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.Tools;
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
        var toolRepository = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        await SeedAgentDefinitionsAsync(repository, cancellationToken);
        await SeedToolDefinitionsAsync(toolRepository, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static async Task SeedAgentDefinitionsAsync(IAgentDefinitionRepository repository, CancellationToken cancellationToken)
    {
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

    private static async Task SeedToolDefinitionsAsync(IToolDefinitionRepository toolRepository, CancellationToken cancellationToken)
    {
        if (await toolRepository.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;

        var echoContext = new ToolDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = "echo_context",
            DisplayName = "Echo Context",
            Description = "Returns the current execution context as JSON. Useful for debugging.",
            InputSchema = """{"type":"object","properties":{"message":{"type":"string"}},"additionalProperties":false}""",
            SideEffectLevel = "read_only",
            TimeoutMs = 5000,
            AsyncSupported = false,
            ConsistencyMode = "none",
            Enabled = true,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        var asyncTask = new ToolDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = "async_task",
            DisplayName = "Async Task",
            Description = "Demonstrates async tool suspension. Returns a wait token for later resumption.",
            SideEffectLevel = "internal_write",
            TimeoutMs = 30000,
            AsyncSupported = true,
            ConsistencyMode = "eventual",
            Enabled = true,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        await toolRepository.AddAsync(echoContext, cancellationToken);
        await toolRepository.AddAsync(asyncTask, cancellationToken);
    }
}
