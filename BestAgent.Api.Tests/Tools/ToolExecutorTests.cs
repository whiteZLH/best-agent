using BestAgent.Application.Tools;
using BestAgent.Domain.Tools;
using BestAgent.Infrastructure.Tools;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.Tools;

public class ToolExecutorTests
{
    private readonly ToolRegistry _toolRegistry = new();
    private readonly IToolDefinitionRepository _toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
    private readonly IHttpToolInvoker _httpToolInvoker = Substitute.For<IHttpToolInvoker>();
    private readonly ToolExecutionContext _context = new("run-001", "writer", "say hi");

    [Fact]
    public async Task ExecuteAsync_ShouldPreferToolDefinitionWebhookOverRegisteredHandler()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        var definition = CreateToolDefinition();
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
        await _httpToolInvoker.Received(1).InvokeAsync(
            Arg.Is<HttpToolInvocationRequest>(request =>
                request.ToolName == "weather" &&
                request.EndpointUrl == "https://example.com/tools/weather"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionDisabledEvenIfHandlerExists()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(enabled: false));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' is disabled.", exception.Message);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallbackToRegisteredHandler_WhenToolDefinitionHasNoEndpointUrl()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(endpointUrl: "   "));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "input", _context, CancellationToken.None);

        Assert.Equal("from-handler", result.Output);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionHasNoEndpointUrlAndNoHandler()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(endpointUrl: "   "));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' is defined but has no endpoint URL configured and no registered handler.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseRegisteredHandler_WhenToolDefinitionMissing()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "input", _context, CancellationToken.None);

        Assert.Equal("from-handler", result.Output);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionAndHandlerMissing()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' has no tool definition and no registered handler.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldInvokeHttpTool_WhenToolDefinitionExists()
    {
        var definition = CreateToolDefinition();
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
        await _httpToolInvoker.Received(1).InvokeAsync(
            Arg.Is<HttpToolInvocationRequest>(request =>
                request.ToolName == "weather" &&
                request.EndpointUrl == "https://example.com/tools/weather" &&
                request.HttpMethod == "POST" &&
                request.AuthHeaders == "{\"Authorization\":\"Bearer token\"}" &&
                request.Input == "{\"city\":\"Shanghai\"}" &&
                request.InputSchema == "{\"type\":\"object\"}" &&
                request.OutputSchema == "{\"type\":\"string\"}" &&
                request.Context == _context &&
                request.TimeoutMs == 5000),
            Arg.Any<CancellationToken>());
    }

    private ToolExecutor CreateExecutor()
    {
        return new ToolExecutor(_toolRegistry, _toolDefinitionRepository, _httpToolInvoker);
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
