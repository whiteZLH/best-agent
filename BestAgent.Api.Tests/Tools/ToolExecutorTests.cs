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
    public async Task ExecuteAsync_ShouldPreferRegisteredHandlerOverToolDefinition()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "input", _context, CancellationToken.None);

        Assert.Equal("weather", result.ToolName);
        Assert.Equal("from-handler", result.Output);
        Assert.False(result.IsPending);
        await _toolDefinitionRepository.DidNotReceiveWithAnyArgs().GetByToolNameAsync(default!, default);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionMissing()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' has no registered handler and no tool definition.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionDisabled()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(enabled: false));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' is disabled.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenEndpointUrlMissing()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(endpointUrl: "   "));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' has no registered handler and no endpoint URL configured.", exception.Message);
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
