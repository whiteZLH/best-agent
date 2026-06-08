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
                null,
                null,
                " https://example.com/tools/weather ",
                " put ",
                " { \"Authorization\" : \"Bearer token\" } ",
                " weather-callback-secret ",
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
        Assert.Equal(ToolExecutionBindingHelper.Webhook, result.ExecutionKind);
        Assert.NotNull(result.ExecutionBinding);
        Assert.Equal("https://example.com/tools/weather", result.EndpointUrl);
        Assert.Equal("PUT", result.HttpMethod);
        Assert.Equal("{\"Authorization\":\"***\"}", result.AuthHeaders);
        Assert.Equal("***", result.CallbackSecret);
        Assert.Equal("read_only", result.SideEffectLevel);
        Assert.Equal("{\"maxAttempts\":2,\"delayMs\":0}", result.RetryPolicy);
        Assert.Equal("{\"scheme\":\"bearer\"}", result.AuthPolicy);
        Assert.Equal("strong", result.ConsistencyMode);
        Assert.Equal("{\"mode\":\"none\"}", result.CompensationPolicy);
        Assert.NotNull(repository.AddedToolDefinition);
        Assert.Equal("weather", repository.AddedToolDefinition!.ToolName);
        Assert.Equal("Weather", repository.AddedToolDefinition.DisplayName);
        Assert.Equal("{ \"type\" : \"object\" }", repository.AddedToolDefinition.InputSchema);
        Assert.Equal("{ \"type\" : \"string\" }", repository.AddedToolDefinition.OutputSchema);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, repository.AddedToolDefinition.ExecutionKind);
        Assert.NotNull(repository.AddedToolDefinition.ExecutionBinding);
        Assert.Equal("https://example.com/tools/weather", repository.AddedToolDefinition.EndpointUrl);
        Assert.Equal("PUT", repository.AddedToolDefinition.HttpMethod);
        Assert.Equal("{ \"Authorization\" : \"Bearer token\" }", repository.AddedToolDefinition.AuthHeaders);
        Assert.Equal("weather-callback-secret", repository.AddedToolDefinition.CallbackSecret);
        Assert.Equal("read_only", repository.AddedToolDefinition.SideEffectLevel);
        Assert.Equal("{\"maxAttempts\":2,\"delayMs\":0}", repository.AddedToolDefinition.RetryPolicy);
        Assert.Equal("{\"scheme\":\"bearer\"}", repository.AddedToolDefinition.AuthPolicy);
        Assert.Equal("strong", repository.AddedToolDefinition.ConsistencyMode);
        Assert.Equal("{\"mode\":\"none\"}", repository.AddedToolDefinition.CompensationPolicy);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldPersistExplicitLocalHandlerBinding()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateToolDefinitionCommand(
                "echo_context",
                "Echo Context",
                "debug tool",
                null,
                null,
                ToolExecutionBindingHelper.LocalHandler,
                "{\"handlerName\":\"echo_context\"}",
                null,
                null,
                null,
                "echo-secret",
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None);

        Assert.Equal(ToolExecutionBindingHelper.LocalHandler, result.ExecutionKind);
        var createdBinding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(
            result.ExecutionBinding,
            nameof(result.ExecutionBinding));
        Assert.Equal("echo_context", createdBinding.HandlerName);
        Assert.Null(result.EndpointUrl);
        Assert.Null(result.HttpMethod);
        Assert.Null(result.AuthHeaders);
        Assert.Equal("***", result.CallbackSecret);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldPersistExplicitInlineResultBinding()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateToolDefinitionCommand(
                "weather_template",
                "Weather Template",
                "fixed weather response",
                "{\"type\":\"object\"}",
                "{\"type\":\"object\"}",
                ToolExecutionBindingHelper.InlineResult,
                "{\"output\":\"{\\\"temperature\\\":26.5}\",\"meta\":\"{\\\"source\\\":\\\"inline\\\"}\"}",
                null,
                null,
                null,
                null,
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None);

        Assert.Equal(ToolExecutionBindingHelper.InlineResult, result.ExecutionKind);
        var createdBinding = ToolExecutionBindingHelper.ParseInlineResultBinding(
            result.ExecutionBinding,
            nameof(result.ExecutionBinding));
        Assert.Equal("{\"temperature\":26.5}", createdBinding.Output);
        Assert.Equal("{\"source\":\"inline\"}", createdBinding.Meta);
        Assert.Null(result.EndpointUrl);
        Assert.Null(result.HttpMethod);
        Assert.Null(result.AuthHeaders);
        Assert.NotNull(repository.AddedToolDefinition);
        Assert.Equal(ToolExecutionBindingHelper.InlineResult, repository.AddedToolDefinition!.ExecutionKind);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenExecutionBindingProvidedWithoutExecutionKind()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "echo_context",
                "Echo Context",
                null,
                null,
                null,
                null,
                "{\"handlerName\":\"echo_context\"}",
                null,
                null,
                null,
                null,
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("ExecutionKind is required when ExecutionBinding is provided.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenWebhookLegacyEndpointConflictsWithExplicitBinding()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                ToolExecutionBindingHelper.Webhook,
                ToolExecutionBindingHelper.CreateWebhookBinding(
                    "https://binding.example.com/weather",
                    "POST",
                    null),
                "https://flat.example.com/weather",
                null,
                null,
                null,
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("EndpointUrl must match ExecutionBinding for 'webhook' execution kind.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenLocalHandlerIncludesLegacyWebhookFields()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "echo_context",
                "Echo Context",
                null,
                null,
                null,
                ToolExecutionBindingHelper.LocalHandler,
                ToolExecutionBindingHelper.CreateLocalHandlerBinding("echo_context"),
                "https://example.com/tools/echo",
                null,
                null,
                null,
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("EndpointUrl must be omitted when execution kind is 'local_handler'.", exception.Message);
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
            new CreateToolDefinitionCommand("weather", "Weather", null, null, null, null, null, null, null, null, null, "ReadOnly", 5000, null, null, null, false, "Strong", null, true),
            CancellationToken.None));

        Assert.Equal("Tool name 'weather' already exists.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldNormalizeStructuredIdempotencyPolicy()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                null,
                "{ \"enabled\": true }",
                false,
                "Strong",
                null,
                true),
            CancellationToken.None);

        Assert.Equal("{\"enabled\":true}", result.IdempotencyPolicy);
        Assert.NotNull(repository.AddedToolDefinition);
        Assert.Equal("{\"enabled\":true}", repository.AddedToolDefinition!.IdempotencyPolicy);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenIdempotencyPolicyIsInvalid()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                null,
                "{\"enabled\":\"yes\"}",
                false,
                "Strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("IdempotencyPolicy.enabled must be boolean.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldNormalizeStructuredRetryPolicy()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                "{ \"maxAttempts\": 4, \"delayMs\": 250 }",
                null,
                null,
                false,
                "Strong",
                null,
                true),
            CancellationToken.None);

        Assert.Equal("{\"maxAttempts\":4,\"delayMs\":250}", result.RetryPolicy);
        Assert.NotNull(repository.AddedToolDefinition);
        Assert.Equal("{\"maxAttempts\":4,\"delayMs\":250}", repository.AddedToolDefinition!.RetryPolicy);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenRetryPolicyIsInvalid()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                "{\"maxAttempts\":0}",
                null,
                null,
                false,
                "Strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("RetryPolicy.maxAttempts must be >= 1.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldNormalizeStructuredAuthAndCompensationPolicies()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                "{ \"Authorization\" : \"Bearer token\" }",
                null,
                "ReadOnly",
                5000,
                null,
                """
                { "scheme": "Bearer" }
                """,
                null,
                false,
                "Strong",
                """
                { "mode": "manual" }
                """,
                true),
            CancellationToken.None);

        Assert.Equal("{\"scheme\":\"bearer\"}", result.AuthPolicy);
        Assert.Equal("{\"mode\":\"manual\"}", result.CompensationPolicy);
        Assert.NotNull(repository.AddedToolDefinition);
        Assert.Equal("{\"scheme\":\"bearer\"}", repository.AddedToolDefinition!.AuthPolicy);
        Assert.Equal("{\"mode\":\"manual\"}", repository.AddedToolDefinition.CompensationPolicy);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenWebhookBearerAuthPolicyMissingBearerHeader()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                "{\"X-Api-Key\":\"token\"}",
                null,
                "ReadOnly",
                5000,
                null,
                "bearer",
                null,
                false,
                "Strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("AuthHeaders must include Authorization Bearer header when AuthPolicy.scheme is 'bearer'.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenWebhookNoAuthPolicyIncludesAuthorizationHeader()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                "{\"Authorization\":\"Bearer token\"}",
                null,
                "ReadOnly",
                5000,
                null,
                "none",
                null,
                false,
                "Strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("AuthHeaders must be omitted when AuthPolicy.scheme is 'none'.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenLocalHandlerAuthPolicyRequiresWebhook()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "echo_context",
                "Echo Context",
                null,
                null,
                null,
                ToolExecutionBindingHelper.LocalHandler,
                ToolExecutionBindingHelper.CreateLocalHandlerBinding("echo_context"),
                null,
                null,
                null,
                null,
                "read_only",
                5000,
                null,
                "oauth",
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("AuthPolicy.scheme 'oauth' is only supported for 'webhook' execution kind.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldNormalizeStructuredParameterPolicy()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var result = await handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                null,
                null,
                false,
                "Strong",
                null,
                true,
                "{\"allowedPaths\":[\"city\",\"unit\"],\"deniedPaths\":[\"debug\"]}"),
            CancellationToken.None);

        Assert.Equal("{\"allowedPaths\":[\"city\",\"unit\"],\"deniedPaths\":[\"debug\"]}", result.ParameterPolicy);
        Assert.NotNull(repository.AddedToolDefinition);
        Assert.Equal("{\"allowedPaths\":[\"city\",\"unit\"],\"deniedPaths\":[\"debug\"]}", repository.AddedToolDefinition!.ParameterPolicy);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenParameterPolicyIsInvalid()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                null,
                null,
                false,
                "Strong",
                null,
                true,
                "{\"allowedPaths\":\"city\"}"),
            CancellationToken.None));

        Assert.Equal("ParameterPolicy.allowedPaths must be an array of non-empty strings.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenStructuredAuthPolicyIsInvalid()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                "{\"scheme\":\"\"}",
                null,
                false,
                "Strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("AuthPolicy.scheme must be non-empty string.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenConsistencyModeMissing()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                null,
                null,
                false,
                "   ",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("ConsistencyMode is required.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenConsistencyModeIsInvalid()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                null,
                null,
                false,
                "linearizable",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("ConsistencyMode must be one of: none, eventual, strong.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenSideEffectLevelIsInvalid()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "weather",
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "mutating",
                5000,
                null,
                null,
                null,
                false,
                "strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("SideEffectLevel must be one of: read_only, internal_write, external_write, destructive.", exception.Message);
    }

    [Fact]
    public async Task CreateToolDefinition_ShouldThrow_WhenDestructiveToolHasNoCompensationPolicy()
    {
        var repository = new FakeToolDefinitionRepository
        {
            ExistsByToolNameAsyncResult = false
        };
        var handler = new CreateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateToolDefinitionCommand(
                "delete_weather",
                "Delete Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather/delete",
                "POST",
                null,
                null,
                "destructive",
                5000,
                null,
                null,
                null,
                false,
                "Strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("CompensationPolicy is required for destructive tools.", exception.Message);
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
                null,
                null,
                " https://example.com/tools/weather-v2 ",
                " patch ",
                " { \"Authorization\" : \"Bearer next-token\" } ",
                " updated-callback-secret ",
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
        Assert.Equal(ToolExecutionBindingHelper.Webhook, result.ExecutionKind);
        Assert.NotNull(result.ExecutionBinding);
        Assert.Equal("https://example.com/tools/weather-v2", result.EndpointUrl);
        Assert.Equal("PATCH", result.HttpMethod);
        Assert.Equal("{\"Authorization\":\"***\"}", result.AuthHeaders);
        Assert.Equal("***", result.CallbackSecret);
        Assert.Equal("internal_write", result.SideEffectLevel);
        Assert.Equal("{\"maxAttempts\":3,\"delayMs\":0}", result.RetryPolicy);
        Assert.Equal("{\"scheme\":\"oauth\"}", result.AuthPolicy);
        Assert.Equal("eventual", result.ConsistencyMode);
        Assert.Equal("{\"mode\":\"manual\"}", result.CompensationPolicy);
        Assert.False(result.AsyncSupported);
        Assert.False(result.Enabled);
        Assert.NotNull(repository.UpdatedToolDefinition);
        Assert.Equal("Weather v2", repository.UpdatedToolDefinition!.DisplayName);
        Assert.Equal("updated description", repository.UpdatedToolDefinition.Description);
        Assert.Equal("{ \"type\" : \"array\" }", repository.UpdatedToolDefinition.InputSchema);
        Assert.Equal("{ \"type\" : \"object\" }", repository.UpdatedToolDefinition.OutputSchema);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, repository.UpdatedToolDefinition.ExecutionKind);
        Assert.NotNull(repository.UpdatedToolDefinition.ExecutionBinding);
        Assert.Equal("https://example.com/tools/weather-v2", repository.UpdatedToolDefinition.EndpointUrl);
        Assert.Equal("PATCH", repository.UpdatedToolDefinition.HttpMethod);
        Assert.Equal("{ \"Authorization\" : \"Bearer next-token\" }", repository.UpdatedToolDefinition.AuthHeaders);
        Assert.Equal("updated-callback-secret", repository.UpdatedToolDefinition.CallbackSecret);
        Assert.Equal("internal_write", repository.UpdatedToolDefinition.SideEffectLevel);
        Assert.Equal("{\"maxAttempts\":3,\"delayMs\":0}", repository.UpdatedToolDefinition.RetryPolicy);
        Assert.Equal("{\"scheme\":\"oauth\"}", repository.UpdatedToolDefinition.AuthPolicy);
        Assert.Equal("eventual", repository.UpdatedToolDefinition.ConsistencyMode);
        Assert.Equal("{\"mode\":\"manual\"}", repository.UpdatedToolDefinition.CompensationPolicy);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldPersistExplicitLocalHandlerBinding()
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
                "Echo Context",
                "debug tool",
                null,
                null,
                ToolExecutionBindingHelper.LocalHandler,
                "{\"handlerName\":\"echo_context\"}",
                null,
                null,
                null,
                "echo-secret-v2",
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None);

        Assert.Equal(ToolExecutionBindingHelper.LocalHandler, result.ExecutionKind);
        var updatedBinding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(
            result.ExecutionBinding,
            nameof(result.ExecutionBinding));
        Assert.Equal("echo_context", updatedBinding.HandlerName);
        Assert.Null(result.EndpointUrl);
        Assert.Null(result.HttpMethod);
        Assert.Null(result.AuthHeaders);
        Assert.Equal("***", result.CallbackSecret);
        Assert.NotNull(repository.UpdatedToolDefinition);
        Assert.Null(repository.UpdatedToolDefinition!.EndpointUrl);
        Assert.Equal("POST", repository.UpdatedToolDefinition.HttpMethod);
        Assert.Null(repository.UpdatedToolDefinition.AuthHeaders);
        Assert.Equal("echo-secret-v2", repository.UpdatedToolDefinition.CallbackSecret);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrow_WhenLocalHandlerAuthPolicyRequiresWebhook()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                "Echo Context",
                null,
                null,
                null,
                ToolExecutionBindingHelper.LocalHandler,
                ToolExecutionBindingHelper.CreateLocalHandlerBinding("echo_context"),
                null,
                null,
                null,
                null,
                "read_only",
                5000,
                null,
                "bearer",
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("AuthPolicy.scheme 'bearer' is only supported for 'webhook' execution kind.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldPersistExplicitInlineResultBinding()
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
                "Weather Template",
                "fixed weather response",
                "{\"type\":\"object\"}",
                "{\"type\":\"object\"}",
                ToolExecutionBindingHelper.InlineResult,
                "{\"output\":\"{\\\"temperature\\\":27.0}\",\"meta\":\"{\\\"source\\\":\\\"inline-v2\\\"}\"}",
                null,
                null,
                null,
                null,
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None);

        Assert.Equal(ToolExecutionBindingHelper.InlineResult, result.ExecutionKind);
        var updatedBinding = ToolExecutionBindingHelper.ParseInlineResultBinding(
            result.ExecutionBinding,
            nameof(result.ExecutionBinding));
        Assert.Equal("{\"temperature\":27.0}", updatedBinding.Output);
        Assert.Equal("{\"source\":\"inline-v2\"}", updatedBinding.Meta);
        Assert.Null(result.EndpointUrl);
        Assert.Null(result.HttpMethod);
        Assert.Null(result.AuthHeaders);
        Assert.NotNull(repository.UpdatedToolDefinition);
        Assert.Equal(ToolExecutionBindingHelper.InlineResult, repository.UpdatedToolDefinition!.ExecutionKind);
        Assert.Null(repository.UpdatedToolDefinition.EndpointUrl);
        Assert.Equal("POST", repository.UpdatedToolDefinition.HttpMethod);
        Assert.Null(repository.UpdatedToolDefinition.AuthHeaders);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrow_WhenStructuredCompensationPolicyIsInvalid()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                null,
                null,
                false,
                "Strong",
                "{\"mode\":\"\"}",
                true),
            CancellationToken.None));

        Assert.Equal("CompensationPolicy.mode must be non-empty string.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrow_WhenConsistencyModeIsInvalid()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "ReadOnly",
                5000,
                null,
                null,
                null,
                false,
                "causal",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("ConsistencyMode must be one of: none, eventual, strong.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrow_WhenSideEffectLevelIsInvalid()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "sync_write",
                5000,
                null,
                null,
                null,
                false,
                "strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("SideEffectLevel must be one of: read_only, internal_write, external_write, destructive.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrow_WhenDestructiveToolHasNoCompensationPolicy()
    {
        var existing = CreateToolDefinition() with
        {
            SideEffectLevel = "read_only",
            CompensationPolicy = "{\"mode\":\"none\"}"
        };
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                "Weather",
                null,
                null,
                null,
                null,
                null,
                "https://example.com/tools/weather",
                "POST",
                null,
                null,
                "destructive",
                5000,
                null,
                null,
                null,
                false,
                "Strong",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("CompensationPolicy is required for destructive tools.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrow_WhenExecutionBindingProvidedWithoutExecutionKind()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                "Echo Context",
                null,
                null,
                null,
                null,
                "{\"handlerName\":\"echo_context\"}",
                null,
                null,
                null,
                null,
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("ExecutionKind is required when ExecutionBinding is provided.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrow_WhenWebhookLegacyHttpMethodConflictsWithExplicitBinding()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                "Weather",
                null,
                null,
                null,
                ToolExecutionBindingHelper.Webhook,
                ToolExecutionBindingHelper.CreateWebhookBinding(
                    "https://example.com/tools/weather",
                    "POST",
                    null),
                null,
                "PATCH",
                null,
                null,
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("HttpMethod must match ExecutionBinding for 'webhook' execution kind.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrow_WhenInlineResultIncludesLegacyWebhookFields()
    {
        var existing = CreateToolDefinition();
        var repository = new FakeToolDefinitionRepository
        {
            GetByIdAsyncResult = existing
        };
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateToolDefinitionCommand(
                existing.Id,
                "Weather Template",
                null,
                null,
                null,
                ToolExecutionBindingHelper.InlineResult,
                ToolExecutionBindingHelper.CreateInlineResultBinding("{\"temperature\":26.5}"),
                null,
                null,
                "{\"Authorization\":\"Bearer token\"}",
                null,
                "read_only",
                5000,
                null,
                null,
                null,
                false,
                "none",
                null,
                true),
            CancellationToken.None));

        Assert.Equal("AuthHeaders must be omitted when execution kind is 'inline_result'.", exception.Message);
    }

    [Fact]
    public async Task UpdateToolDefinition_ShouldThrowNotFound_WhenToolDoesNotExist()
    {
        var repository = new FakeToolDefinitionRepository();
        var handler = new UpdateToolDefinitionCommandHandler(repository);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateToolDefinitionCommand("missing", "Weather", null, null, null, null, null, null, null, null, null, "ReadOnly", 5000, null, null, null, false, "Strong", null, true),
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
            ExecutionKind = ToolExecutionBindingHelper.Webhook,
            ExecutionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://example.com/tools/weather",
                "POST",
                "{\"Authorization\":\"Bearer token\"}"),
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "POST",
            AuthHeaders = "{\"Authorization\":\"Bearer token\"}",
            CallbackSecret = "existing-callback-secret",
            SideEffectLevel = "read_only",
            TimeoutMs = 5000,
            RetryPolicy = "{\"maxAttempts\":2,\"delayMs\":0}",
            AuthPolicy = "{\"scheme\":\"bearer\"}",
            ParameterPolicy = "{\"allowedPaths\":[\"city\"],\"deniedPaths\":[]}",
            IdempotencyPolicy = "idempotent",
            AsyncSupported = true,
            ConsistencyMode = "strong",
            CompensationPolicy = "{\"mode\":\"none\"}",
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
