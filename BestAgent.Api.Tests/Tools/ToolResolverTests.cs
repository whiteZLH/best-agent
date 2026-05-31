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
    public async Task ResolveAsync_ShouldFallbackToLocalHandler_WhenDefinitionHasNoEndpoint()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) => Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(endpointUrl: "   "));
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "input", _context, CancellationToken.None);

        Assert.Equal(ToolExecutionKind.LocalHandler, resolution.ExecutionKind);
        Assert.NotNull(resolution.LocalHandler);
        Assert.Null(resolution.WebhookRequest);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenDefinitionHasNoEndpointAndNoHandler()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(endpointUrl: "   "));
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' is defined but has no endpoint URL configured and no registered handler.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseLocalHandler_WhenDefinitionMissing()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) => Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var resolver = CreateResolver();

        var resolution = await resolver.ResolveAsync("weather", "input", _context, CancellationToken.None);

        Assert.Equal(ToolExecutionKind.LocalHandler, resolution.ExecutionKind);
        Assert.NotNull(resolution.LocalHandler);
        Assert.Null(resolution.Definition);
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenDefinitionAndHandlerMissing()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var resolver = CreateResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' has no tool definition and no registered handler.", exception.Message);
    }

    private ToolResolver CreateResolver()
    {
        return new ToolResolver(_toolRegistry, _toolDefinitionRepository);
    }

    private static ToolDefinition CreateToolDefinition(bool enabled = true, string? endpointUrl = "https://example.com/tools/weather")
    {
        return new ToolDefinition
        {
            Id = "tool-001",
            ToolName = "weather",
            DisplayName = "Weather",
            Enabled = enabled,
            EndpointUrl = endpointUrl,
            HttpMethod = "POST",
            AuthHeaders = "{\"Authorization\":\"Bearer token\"}",
            InputSchema = "{\"type\":\"object\"}",
            OutputSchema = "{\"type\":\"string\"}",
            TimeoutMs = 5000
        };
    }
}
