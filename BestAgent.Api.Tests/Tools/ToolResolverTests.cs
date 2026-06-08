using BestAgent.Application.Tools;
using BestAgent.Domain.Tools;
using BestAgent.Infrastructure.Tools;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.Tools;

public class ToolResolverTests
{
    private readonly ToolRegistry _toolRegistry = new();
    private readonly IToolDefinitionRepository _toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
    private readonly ToolExecutionContext _context = new("run-001", "writer", "say hi");

    [Fact]
    public async Task ResolveAsync_ShouldPreferWebhook_WhenDefinitionHasEndpoint()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) => Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition());
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal(ToolExecutionKind.Webhook, resolution.ExecutionKind);
        Assert.NotNull(resolution.WebhookRequest);
        Assert.Null(resolution.LocalHandler);
        Assert.Equal("https://example.com/tools/weather", resolution.WebhookRequest!.EndpointUrl);
        Assert.Null(resolution.WebhookRequest.IdempotencyKey);
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseExplicitWebhookBinding_WhenExecutionKindPresent()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(
                endpointUrl: null,
                executionKind: ToolExecutionBindingHelper.Webhook,
                executionBinding: ToolExecutionBindingHelper.CreateWebhookBinding(
                    "https://binding.example.com/weather",
                    "PATCH",
                    "{\"Authorization\":\"Bearer binding-token\"}")));
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal(ToolExecutionKind.Webhook, resolution.ExecutionKind);
        Assert.NotNull(resolution.WebhookRequest);
        Assert.Equal("https://binding.example.com/weather", resolution.WebhookRequest!.EndpointUrl);
        Assert.Equal("PATCH", resolution.WebhookRequest.HttpMethod);
        Assert.Equal("{\"Authorization\":\"Bearer binding-token\"}", resolution.WebhookRequest.AuthHeaders);
    }

    [Fact]
    public async Task ResolveAsync_ShouldAcceptLegacyWebhookBindingShape_WhenExecutionKindPresent()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(
                endpointUrl: null,
                executionKind: ToolExecutionBindingHelper.Webhook,
                executionBinding: "{\"endpointUrl\":\"https://legacy.example.com/weather\",\"httpMethod\":\"patch\",\"authHeaders\":\"{\\\"Authorization\\\":\\\"Bearer legacy-token\\\"}\"}"));
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal(ToolExecutionKind.Webhook, resolution.ExecutionKind);
        Assert.NotNull(resolution.WebhookRequest);
        Assert.Equal("https://legacy.example.com/weather", resolution.WebhookRequest!.EndpointUrl);
        Assert.Equal("PATCH", resolution.WebhookRequest.HttpMethod);
        Assert.Equal("{\"Authorization\":\"Bearer legacy-token\"}", resolution.WebhookRequest.AuthHeaders);
    }

    [Fact]
    public async Task ResolveAsync_ShouldGenerateIdempotencyKey_WhenPolicyEnabled()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(idempotencyPolicy: "idempotent"));
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.NotNull(resolution.WebhookRequest);
        Assert.False(string.IsNullOrWhiteSpace(resolution.WebhookRequest!.IdempotencyKey));
        Assert.Matches("^[a-f0-9]{32}$", resolution.WebhookRequest.IdempotencyKey!);
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotGenerateIdempotencyKey_WhenPolicyDisabled()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(idempotencyPolicy: "non-idempotent"));
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.NotNull(resolution.WebhookRequest);
        Assert.Null(resolution.WebhookRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseExplicitLocalHandlerBinding_WhenExecutionKindPresent()
    {
        _toolRegistry.RegisterHandler("custom-weather-handler", (_, _, _) => Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(
                endpointUrl: null,
                executionKind: ToolExecutionBindingHelper.LocalHandler,
                executionBinding: ToolExecutionBindingHelper.CreateLocalHandlerBinding("custom-weather-handler")));
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "input", _context, CancellationToken.None);

        Assert.Equal(ToolExecutionKind.LocalHandler, resolution.ExecutionKind);
        Assert.NotNull(resolution.LocalHandler);
        Assert.Null(resolution.WebhookRequest);
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseExplicitInlineResultBinding_WhenExecutionKindPresent()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(
                endpointUrl: null,
                executionKind: ToolExecutionBindingHelper.InlineResult,
                executionBinding: ToolExecutionBindingHelper.CreateInlineResultBinding(
                    "{\"temperature\":26.5}",
                    "{\"source\":\"inline\"}")));
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "input", _context, CancellationToken.None);

        Assert.Equal(ToolExecutionKind.InlineResult, resolution.ExecutionKind);
        Assert.NotNull(resolution.InlineResultRequest);
        Assert.Null(resolution.LocalHandler);
        Assert.Null(resolution.WebhookRequest);
        Assert.Equal("{\"temperature\":26.5}", resolution.InlineResultRequest!.Output);
        Assert.Equal("{\"source\":\"inline\"}", resolution.InlineResultRequest.Meta);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenWebhookAuthPolicyRequiresBearerHeader()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(
                endpointUrl: null,
                executionKind: ToolExecutionBindingHelper.Webhook,
                executionBinding: ToolExecutionBindingHelper.CreateWebhookBinding(
                    "https://example.com/tools/weather",
                    "POST",
                    "{\"X-Api-Key\":\"token\"}"),
                authPolicy: "bearer"));
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("AuthHeaders must include Authorization Bearer header when AuthPolicy.scheme is 'bearer'.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenLocalHandlerAuthPolicyRequiresWebhook()
    {
        _toolRegistry.RegisterHandler("custom-weather-handler", (_, _, _) => Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(
                endpointUrl: null,
                executionKind: ToolExecutionBindingHelper.LocalHandler,
                executionBinding: ToolExecutionBindingHelper.CreateLocalHandlerBinding("custom-weather-handler"),
                authPolicy: "oauth",
                authHeaders: null));
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("AuthPolicy.scheme 'oauth' is only supported for 'webhook' execution kind.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenDefinitionDisabledEvenIfHandlerExists()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) => Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(enabled: false));
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' is disabled.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenDefinitionHasNoExplicitBindingAndNoLegacyEndpoint()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) => Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(endpointUrl: "   "));
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' is defined but has no explicit execution binding configured.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenExecutionBindingVersionIsUnsupported()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(
                endpointUrl: null,
                executionKind: ToolExecutionBindingHelper.Webhook,
                executionBinding: "{\"version\":99,\"type\":\"webhook\",\"webhook\":{\"endpointUrl\":\"https://example.com/tools/weather\",\"httpMethod\":\"POST\"}}"));
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("ExecutionBinding uses unsupported binding version '99'.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenDefinitionMissingEvenIfHandlerExists()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) => Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' has no persisted tool definition.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenDefinitionAndHandlerMissing()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' has no persisted tool definition.", exception.Message);
    }

    private ToolResolver CreateResolver()
    {
        return new ToolResolver(_toolRegistry, _toolDefinitionRepository);
    }

    private static ToolDefinition CreateToolDefinition(
        bool enabled = true,
        string? endpointUrl = "https://example.com/tools/weather",
        string? executionKind = null,
        string? executionBinding = null,
        string? idempotencyPolicy = null,
        string? authPolicy = null,
        string? authHeaders = "{\"Authorization\":\"Bearer token\"}")
    {
        if (executionKind is null
            && executionBinding is null
            && !string.IsNullOrWhiteSpace(endpointUrl))
        {
            executionKind = ToolExecutionBindingHelper.Webhook;
            executionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                endpointUrl.Trim(),
                "POST",
                authHeaders);
        }

        return new ToolDefinition
        {
            Id = "tool-001",
            ToolName = "weather",
            DisplayName = "Weather",
            Enabled = enabled,
            ExecutionKind = executionKind,
            ExecutionBinding = executionBinding,
            EndpointUrl = endpointUrl,
            HttpMethod = "POST",
            AuthHeaders = authHeaders,
            InputSchema = "{\"type\":\"object\"}",
            OutputSchema = "{\"type\":\"string\"}",
            AuthPolicy = authPolicy,
            IdempotencyPolicy = idempotencyPolicy,
            TimeoutMs = 5000
        };
    }
}
