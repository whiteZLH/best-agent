using BestAgent.Application.Exceptions;
using BestAgent.Application.Tools;
using BestAgent.Application.Tools.Commands.CreateToolDefinition;
using BestAgent.Application.Tools.Commands.DeleteToolDefinition;
using BestAgent.Application.Tools.Commands.UpdateToolDefinition;
using BestAgent.Domain.Tools;
using Xunit;

namespace BestAgent.Api.Tests.Tools.Commands;

public class ToolDefinitionCommandHandlerTests
{
    [Fact]
    public async Task CreateToolDefinition_ShouldTrimNormalizeAndPersist()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateToolDefinitionCommand(
                " weather ",
                " Weather ",
                " current weather ",
                " { \"type\" : \"object\" } ",
                " { \"type\" : \"string\" } ",
                " https://example.com/tools/weather ",
                " put ",
                " { \"Authorization\" : \"Bearer token\" } ",
                " ReadOnly ",
                5000,
                "retry-once",
                "bearer",
                "idempotent",
                true,
                " Strong ",
                "none",
                true),
            CancellationToken.None);

        Assert.Equal("weather", result.ToolName);
        Assert.Equal("Weather", result.DisplayName);
        Assert.Equal("current weather", result.Description);
        Assert.Equal("{ \"type\" : \"object\" }", result.InputSchema);
        Assert.Equal("{ \"type\" : \"string\" }", result.OutputSchema);
        Assert.Equal("https://example.com/tools/weather", result.EndpointUrl);
        Assert.Equal("PUT", result.HttpMethod);
        Assert.Equal("{ \"Authorization\" : \"Bearer token\" }", result.AuthHeaders);
        Assert.Equal("ReadOnly", result.SideEffectLevel);
        Assert.Equal("Strong", result.ConsistencyMode);
        Assert.NotNull(repository.AddedToolDefinition);
        Assert.Equal("weather", repository.AddedToolDefinition!.ToolName);
        Assert.Equal("Weather", repository.AddedToolDefinition.DisplayName);
        Assert.Equal("{ \"type\" : \"object\" }", repository.AddedToolDefinition.InputSchema);
        Assert.Equal("{ \"type\" : \"string\" }", repository.AddedToolDefinition.OutputSchema);
        Assert.Equal("https://example.com/tools/weather", repository.AddedToolDefinition.EndpointUrl);
        Assert.Equal("PUT", repository.AddedToolDefinition.HttpMethod);
        Assert.Equal("{ \"Authorization\" : \"Bearer token\" }", repository.AddedToolDefinition.AuthHeaders);
        Assert.Equal("ReadOnly", repository.AddedToolDefinition.SideEffectLevel);
        Assert.Equal("Strong", repository.AddedToolDefinition.ConsistencyMode);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenToolNameAlreadyExists()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = true
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand("weather", "Weather", null, null, null, null, null, null, "ReadOnly", 5000, null, null, null, false, "Strong", null, true),
            CancellationToken.None));

        Assert.Equal("Tool name 'weather' already exists.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldUpdatePersistedEntity()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                " Weather v2 ",
                " updated description ",
                " { \"type\" : \"array\" } ",
                " { \"type\" : \"object\" } ",
                " https://example.com/tools/weather-v2 ",
                " patch ",
                " { \"Authorization\" : \"Bearer next-token\" } ",
                " InternalWrite ",
                8000,
                "retry-twice",
                "oauth",
                "non-idempotent",
                false,
                " Eventual ",
                "manual",
                false),
            CancellationToken.None);

        Assert.Equal(existing.Id, result.Id);
        Assert.Equal("weather", result.ToolName);
        Assert.Equal("Weather v2", result.DisplayName);
        Assert.Equal("updated description", result.Description);
        Assert.Equal("{ \"type\" : \"array\" }", result.InputSchema);
        Assert.Equal("{ \"type\" : \"object\" }", result.OutputSchema);
        Assert.Equal("https://example.com/tools/weather-v2", result.EndpointUrl);
        Assert.Equal("PATCH", result.HttpMethod);
        Assert.Equal("{ \"Authorization\" : \"Bearer next-token\" }", result.AuthHeaders);
        Assert.Equal("InternalWrite", result.SideEffectLevel);
        Assert.Equal("Eventual", result.ConsistencyMode);
        Assert.False(result.AsyncSupported);
        Assert.False(result.Enabled);
        Assert.NotNull(repository.UpdatedToolDefinition);
        Assert.Equal("Weather v2", repository.UpdatedToolDefinition!.DisplayName);
        Assert.Equal("updated description", repository.UpdatedToolDefinition.Description);
        Assert.Equal("{ \"type\" : \"array\" }", repository.UpdatedToolDefinition.InputSchema);
        Assert.Equal("{ \"type\" : \"object\" }", repository.UpdatedToolDefinition.OutputSchema);
        Assert.Equal("https://example.com/tools/weather-v2", repository.UpdatedToolDefinition.EndpointUrl);
        Assert.Equal("PATCH", repository.UpdatedToolDefinition.HttpMethod);
        Assert.Equal("{ \"Authorization\" : \"Bearer next-token\" }", repository.UpdatedToolDefinition.AuthHeaders);
        Assert.Equal("InternalWrite", repository.UpdatedToolDefinition.SideEffectLevel);
        Assert.Equal("Eventual", repository.UpdatedToolDefinition.ConsistencyMode);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrowNotFound_WhenToolDoesNotExist()
    {
        var repository = new FakeToolDefinitionRepository();
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateToolDefinitionCommand("missing", "Weather", null, null, null, null, null, null, "ReadOnly", 5000, null, null, null, false, "Strong", null, true),
            CancellationToken.None));

        Assert.Equal("Entity 'ToolDefinition' with key 'missing' was not found.", exception.Message);
    }

    [Fact]
    public async Task DeleteToolDefinition_ShouldDeleteExistingEntity()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new DeleteToolDefinitionCommandHandler(repository);

        await handler.Handle(new DeleteToolDefinitionCommand(existing.Id), CancellationToken.None);

        Assert.Equal(existing, repository.DeletedToolDefinition);
    }

    [Fact]
    public async Task DeleteToolDefinition_ShouldThrowNotFound_WhenToolDoesNotExist()
    {
        var repository = new FakeToolDefinitionRepository();
        var handler = new DeleteToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new DeleteToolDefinitionCommand("missing"),
            CancellationToken.None));

        Assert.Equal("Entity 'ToolDefinition' with key 'missing' was not found.", exception.Message);
    }

    private static ToolDefinition CreateToolDefinition()
    {
        var now = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
        return new ToolDefinition
        {
            Id = "tool-001",
            ToolName = "weather",
            DisplayName = "Weather",
            Description = "current weather",
            InputSchema = "{\"type\":\"object\"}",
            OutputSchema = "{\"type\":\"string\"}",
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "POST",
            AuthHeaders = "{\"Authorization\":\"Bearer token\"}",
            SideEffectLevel = "ReadOnly",
            TimeoutMs = 5000,
            RetryPolicy = "retry-once",
            AuthPolicy = "bearer",
            IdempotencyPolicy = "idempotent",
            AsyncSupported = true,
            ConsistencyMode = "Strong",
            CompensationPolicy = "none",
            Enabled = true,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }

    private sealed class FakeToolDefinitionRepository : IToolDefinitionRepository
    {
        public ToolDefinition? GetByIdAsyncResult { get; set; }
        public bool ExistsByToolNameAsyncResult { get; set; }
        public ToolDefinition? AddedToolDefinition { get; private set; }
        public ToolDefinition? UpdatedToolDefinition { get; private set; }
        public ToolDefinition? DeletedToolDefinition { get; private set; }

        public Task<ToolDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetByIdAsyncResult);
        }

        public Task<ToolDefinition?> GetByToolNameAsync(string toolName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ToolDefinition>> GetEnabledAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsByToolNameAsync(string toolName, CancellationToken cancellationToken)
        {
            return Task.FromResult(ExistsByToolNameAsyncResult);
        }

        public Task<bool> AnyAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AddAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
        {
            AddedToolDefinition = toolDefinition;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
        {
            UpdatedToolDefinition = toolDefinition;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken)
        {
            DeletedToolDefinition = toolDefinition;
            return Task.CompletedTask;
        }
    }
}
