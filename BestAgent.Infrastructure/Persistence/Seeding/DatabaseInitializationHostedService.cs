using BestAgent.Application.Tools;
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
        await NormalizeLegacyToolDefinitionsAsync(toolRepository, cancellationToken);
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
            DeniedTools = "[]",
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
        var now = DateTime.UtcNow;
        await EnsureToolDefinitionAsync(toolRepository, CreateEchoContext(now), cancellationToken);
        await EnsureToolDefinitionAsync(toolRepository, CreateAsyncTask(now), cancellationToken);
    }

    private static async Task EnsureToolDefinitionAsync(
        IToolDefinitionRepository toolRepository,
        ToolDefinition toolDefinition,
        CancellationToken cancellationToken)
    {
        if (await toolRepository.ExistsByToolNameAsync(toolDefinition.ToolName, cancellationToken))
        {
            return;
        }

        await toolRepository.AddAsync(toolDefinition, cancellationToken);
    }

    private static async Task NormalizeLegacyToolDefinitionsAsync(
        IToolDefinitionRepository toolRepository,
        CancellationToken cancellationToken)
    {
        var toolDefinitions = await toolRepository.GetAllAsync(cancellationToken);
        foreach (var toolDefinition in toolDefinitions)
        {
            var executionSettings = string.IsNullOrWhiteSpace(toolDefinition.ExecutionKind)
                && string.IsNullOrWhiteSpace(toolDefinition.ExecutionBinding)
                && string.IsNullOrWhiteSpace(toolDefinition.EndpointUrl)
                    ? new PersistedToolExecutionSettings(
                        toolDefinition.ExecutionKind,
                        toolDefinition.ExecutionBinding,
                        toolDefinition.EndpointUrl,
                        toolDefinition.HttpMethod,
                        toolDefinition.AuthHeaders)
                    : ToolExecutionBindingHelper.NormalizePersistedExecutionSettingsForStorage(
                        toolDefinition.ExecutionKind,
                        toolDefinition.ExecutionBinding,
                        toolDefinition.EndpointUrl,
                        toolDefinition.HttpMethod,
                        toolDefinition.AuthHeaders,
                        nameof(toolDefinition.ExecutionKind),
                        nameof(toolDefinition.ExecutionBinding),
                        nameof(toolDefinition.EndpointUrl),
                        nameof(toolDefinition.HttpMethod),
                        nameof(toolDefinition.AuthHeaders));
            var policySettings = ToolPolicySettingsHelper.NormalizePersistedPolicySettings(
                toolDefinition.RetryPolicy,
                toolDefinition.AuthPolicy,
                toolDefinition.ParameterPolicy,
                toolDefinition.IdempotencyPolicy,
                toolDefinition.CompensationPolicy,
                toolDefinition.ConsistencyMode,
                toolDefinition.SideEffectLevel,
                nameof(toolDefinition.RetryPolicy),
                nameof(toolDefinition.AuthPolicy),
                nameof(toolDefinition.ParameterPolicy),
                nameof(toolDefinition.IdempotencyPolicy),
                nameof(toolDefinition.CompensationPolicy),
                nameof(toolDefinition.ConsistencyMode),
                nameof(toolDefinition.SideEffectLevel),
                executionSettings.ExecutionKind,
                executionSettings.AuthHeaders,
                nameof(toolDefinition.ExecutionKind),
                nameof(toolDefinition.AuthHeaders));

            if (string.Equals(toolDefinition.ExecutionKind, executionSettings.ExecutionKind, StringComparison.Ordinal)
                && string.Equals(toolDefinition.ExecutionBinding, executionSettings.ExecutionBinding, StringComparison.Ordinal)
                && string.Equals(toolDefinition.EndpointUrl, executionSettings.EndpointUrl, StringComparison.Ordinal)
                && string.Equals(toolDefinition.HttpMethod, executionSettings.HttpMethod, StringComparison.Ordinal)
                && string.Equals(toolDefinition.AuthHeaders, executionSettings.AuthHeaders, StringComparison.Ordinal)
                && string.Equals(toolDefinition.RetryPolicy, policySettings.RetryPolicy, StringComparison.Ordinal)
                && string.Equals(toolDefinition.AuthPolicy, policySettings.AuthPolicy, StringComparison.Ordinal)
                && string.Equals(toolDefinition.ParameterPolicy, policySettings.ParameterPolicy, StringComparison.Ordinal)
                && string.Equals(toolDefinition.IdempotencyPolicy, policySettings.IdempotencyPolicy, StringComparison.Ordinal)
                && string.Equals(toolDefinition.CompensationPolicy, policySettings.CompensationPolicy, StringComparison.Ordinal)
                && string.Equals(toolDefinition.ConsistencyMode, policySettings.ConsistencyMode, StringComparison.Ordinal)
                && string.Equals(toolDefinition.SideEffectLevel, policySettings.SideEffectLevel, StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = toolDefinition with
            {
                ExecutionKind = executionSettings.ExecutionKind,
                ExecutionBinding = executionSettings.ExecutionBinding,
                EndpointUrl = executionSettings.EndpointUrl,
                HttpMethod = executionSettings.HttpMethod,
                AuthHeaders = executionSettings.AuthHeaders,
                RetryPolicy = policySettings.RetryPolicy,
                AuthPolicy = policySettings.AuthPolicy,
                ParameterPolicy = policySettings.ParameterPolicy,
                IdempotencyPolicy = policySettings.IdempotencyPolicy,
                CompensationPolicy = policySettings.CompensationPolicy,
                ConsistencyMode = policySettings.ConsistencyMode,
                SideEffectLevel = policySettings.SideEffectLevel,
                LastModifier = "system",
                LastModifierName = "system",
                LastModifyTime = DateTime.UtcNow
            };
            await toolRepository.UpdateAsync(normalized, cancellationToken);
        }
    }

    private static ToolDefinition CreateEchoContext(DateTime now)
    {
        return new ToolDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = "echo_context",
            DisplayName = "Echo Context",
            Description = "Returns the current execution context as JSON. Useful for debugging.",
            InputSchema = """{"type":"object","properties":{"message":{"type":"string"}},"additionalProperties":false}""",
            ExecutionKind = ToolExecutionBindingHelper.LocalHandler,
            ExecutionBinding = ToolExecutionBindingHelper.CreateLocalHandlerBinding("echo_context"),
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
    }

    private static ToolDefinition CreateAsyncTask(DateTime now)
    {
        return new ToolDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = "async_task",
            DisplayName = "Async Task",
            Description = "Demonstrates async tool suspension. Returns a wait token for later resumption.",
            ExecutionKind = ToolExecutionBindingHelper.LocalHandler,
            ExecutionBinding = ToolExecutionBindingHelper.CreateLocalHandlerBinding("async_task"),
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
    }
}
