using BestAgent.Application.Tools;
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
        toolDefinitionRepository.ExistsByToolNameAsync("echo_context", Arg.Any<CancellationToken>()).Returns(false);
        toolDefinitionRepository.ExistsByToolNameAsync("async_task", Arg.Any<CancellationToken>()).Returns(false);
        toolDefinitionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ToolDefinition>());

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

        await toolDefinitionRepository.Received(1).AddAsync(
            Arg.Is<ToolDefinition>(tool =>
                tool.ToolName == "echo_context" &&
                tool.ExecutionKind == ToolExecutionBindingHelper.LocalHandler &&
                tool.SideEffectLevel == "read_only" &&
                tool.Enabled),
            Arg.Any<CancellationToken>());
        await toolDefinitionRepository.Received(1).AddAsync(
            Arg.Is<ToolDefinition>(tool =>
                tool.ToolName == "async_task" &&
                tool.ExecutionKind == ToolExecutionBindingHelper.LocalHandler &&
                tool.SideEffectLevel == "internal_write" &&
                tool.AsyncSupported),
            Arg.Any<CancellationToken>());

        var addedTools = toolDefinitionRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IToolDefinitionRepository.AddAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<ToolDefinition>()
            .Where(tool => tool.ExecutionKind == ToolExecutionBindingHelper.LocalHandler)
            .ToArray();
        Assert.Contains(addedTools, tool =>
            ToolExecutionBindingHelper.ParseLocalHandlerBinding(tool.ExecutionBinding, nameof(tool.ExecutionBinding)).HandlerName == "echo_context");
        Assert.Contains(addedTools, tool =>
            ToolExecutionBindingHelper.ParseLocalHandlerBinding(tool.ExecutionBinding, nameof(tool.ExecutionBinding)).HandlerName == "async_task");
    }

    [Fact]
    public async Task StartAsync_ShouldEnsureMissingBuiltInTools_WhenOtherToolsAlreadyExist()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("echo_context", Arg.Any<CancellationToken>()).Returns(false);
        toolDefinitionRepository.ExistsByToolNameAsync("async_task", Arg.Any<CancellationToken>()).Returns(false);
        toolDefinitionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ToolDefinition>());

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        await agentDefinitionRepository.DidNotReceive().AddAsync(Arg.Any<ResolvedAgentDefinition>(), Arg.Any<CancellationToken>());
        await toolDefinitionRepository.Received(2).AddAsync(Arg.Any<ToolDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldSkipBuiltInTool_WhenItAlreadyExists()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("echo_context", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("async_task", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ToolDefinition>());

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        await toolDefinitionRepository.DidNotReceive().AddAsync(Arg.Any<ToolDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldNormalizeLegacyWebhookToolDefinitions_WhenPersistedBindingMissing()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var legacyTool = new ToolDefinition
        {
            Id = "tool-legacy",
            ToolName = "weather",
            DisplayName = "Weather",
            Enabled = true,
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "patch",
            AuthHeaders = "{ \"Authorization\" : \"Bearer token\" }",
            TimeoutMs = 5000,
            SideEffectLevel = "read_only",
            ConsistencyMode = "none",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("echo_context", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("async_task", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([legacyTool]);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        var updatedTool = toolDefinitionRepository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IToolDefinitionRepository.UpdateAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<ToolDefinition>()
            .Single();
        var binding = ToolExecutionBindingHelper.ParseWebhookBinding(
            updatedTool.ExecutionBinding,
            nameof(ToolDefinition.ExecutionBinding));

        await toolDefinitionRepository.Received(1).UpdateAsync(
            Arg.Is<ToolDefinition>(tool =>
                tool.Id == "tool-legacy" &&
                tool.ExecutionKind == ToolExecutionBindingHelper.Webhook &&
                tool.EndpointUrl == "https://example.com/tools/weather" &&
                tool.HttpMethod == "PATCH" &&
                tool.AuthHeaders == "{ \"Authorization\" : \"Bearer token\" }"),
            Arg.Any<CancellationToken>());
        Assert.Equal("https://example.com/tools/weather", binding.EndpointUrl);
        Assert.Equal("PATCH", binding.HttpMethod);
        Assert.Equal("{ \"Authorization\" : \"Bearer token\" }", binding.AuthHeaders);
    }

    [Fact]
    public async Task StartAsync_ShouldNormalizeWebhookFlatFields_FromPersistedBinding_WhenLegacyFieldsDrifted()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var persistedTool = new ToolDefinition
        {
            Id = "tool-webhook",
            ToolName = "weather",
            DisplayName = "Weather",
            Enabled = true,
            ExecutionKind = ToolExecutionBindingHelper.Webhook,
            ExecutionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://binding.example.com/tools/weather",
                "PATCH",
                "{ \"Authorization\" : \"Bearer binding-token\" }"),
            EndpointUrl = "https://legacy.example.com/tools/weather",
            HttpMethod = "post",
            AuthHeaders = "{ \"Authorization\" : \"Bearer legacy-token\" }",
            TimeoutMs = 5000,
            SideEffectLevel = "read_only",
            ConsistencyMode = "none",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("echo_context", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("async_task", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([persistedTool]);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        await toolDefinitionRepository.Received(1).UpdateAsync(
            Arg.Is<ToolDefinition>(tool =>
                tool.Id == "tool-webhook" &&
                tool.ExecutionKind == ToolExecutionBindingHelper.Webhook &&
                tool.EndpointUrl == "https://binding.example.com/tools/weather" &&
                tool.HttpMethod == "PATCH" &&
                tool.AuthHeaders == "{ \"Authorization\" : \"Bearer binding-token\" }"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldClearLegacyWebhookFlatFields_ForPersistedLocalHandlerBinding()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var persistedTool = new ToolDefinition
        {
            Id = "tool-local",
            ToolName = "echo_context",
            DisplayName = "Echo Context",
            Enabled = true,
            ExecutionKind = ToolExecutionBindingHelper.LocalHandler,
            ExecutionBinding = ToolExecutionBindingHelper.CreateLocalHandlerBinding("echo_context"),
            EndpointUrl = "https://legacy.example.com/tools/echo",
            HttpMethod = "post",
            AuthHeaders = "{ \"Authorization\" : \"Bearer legacy-token\" }",
            TimeoutMs = 5000,
            SideEffectLevel = "read_only",
            ConsistencyMode = "none",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("echo_context", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("async_task", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([persistedTool]);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        await toolDefinitionRepository.Received(1).UpdateAsync(
            Arg.Is<ToolDefinition>(tool =>
                tool.Id == "tool-local" &&
                tool.ExecutionKind == ToolExecutionBindingHelper.LocalHandler &&
                tool.EndpointUrl == null &&
                tool.HttpMethod == "POST" &&
                tool.AuthHeaders == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldNormalizeLegacyPolicyFields_ForPersistedToolDefinition()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var persistedTool = new ToolDefinition
        {
            Id = "tool-policy",
            ToolName = "weather",
            DisplayName = "Weather",
            Enabled = true,
            ExecutionKind = ToolExecutionBindingHelper.Webhook,
            ExecutionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://example.com/tools/weather",
                "POST",
                "{ \"Authorization\" : \"Bearer token\" }"),
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "POST",
            AuthHeaders = "{ \"Authorization\" : \"Bearer token\" }",
            TimeoutMs = 5000,
            RetryPolicy = "retry-once",
            AuthPolicy = "Bearer",
            IdempotencyPolicy = "disabled",
            CompensationPolicy = "manual",
            SideEffectLevel = "ReadOnly",
            ConsistencyMode = "Strong",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("echo_context", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("async_task", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([persistedTool]);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        await toolDefinitionRepository.Received(1).UpdateAsync(
            Arg.Is<ToolDefinition>(tool =>
                tool.Id == "tool-policy" &&
                tool.RetryPolicy == "{\"maxAttempts\":2,\"delayMs\":0}" &&
                tool.AuthPolicy == "{\"scheme\":\"bearer\"}" &&
                tool.IdempotencyPolicy == "non-idempotent" &&
                tool.CompensationPolicy == "{\"mode\":\"manual\"}" &&
                tool.SideEffectLevel == "read_only" &&
                tool.ConsistencyMode == "strong"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldThrow_WhenPersistedWebhookAuthPolicyRequiresBearerHeader()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new BestAgentDbContext(options);
        var agentDefinitionRepository = Substitute.For<IAgentDefinitionRepository>();
        var toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
        var persistedTool = new ToolDefinition
        {
            Id = "tool-policy-invalid",
            ToolName = "weather",
            DisplayName = "Weather",
            Enabled = true,
            ExecutionKind = ToolExecutionBindingHelper.Webhook,
            ExecutionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://example.com/tools/weather",
                "POST",
                "{\"X-Api-Key\":\"token\"}"),
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "POST",
            AuthHeaders = "{\"X-Api-Key\":\"token\"}",
            TimeoutMs = 5000,
            AuthPolicy = "bearer",
            SideEffectLevel = "read_only",
            ConsistencyMode = "none",
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow
        };

        agentDefinitionRepository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("echo_context", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.ExistsByToolNameAsync("async_task", Arg.Any<CancellationToken>()).Returns(true);
        toolDefinitionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([persistedTool]);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(agentDefinitionRepository);
        services.AddSingleton(toolDefinitionRepository);
        await using var serviceProvider = services.BuildServiceProvider();

        var hostedService = new DatabaseInitializationHostedService(serviceProvider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            hostedService.StartAsync(CancellationToken.None));

        Assert.Equal("AuthHeaders must include Authorization Bearer header when AuthPolicy.scheme is 'bearer'.", exception.Message);
    }
}
