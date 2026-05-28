using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BestAgent.Application.Tools;
using BestAgent.Infrastructure.Tools;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.Tools;

public class HttpToolInvokerTests
{
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
    private readonly ToolExecutionContext _context = new("run-001", "writer", "say hi");

    [Fact]
    public async Task InvokeAsync_ShouldSendExpectedRequest()
    {
        HttpMethod? capturedMethod = null;
        string? capturedUri = null;
        string[]? capturedAuthValues = null;
        JsonElement? capturedPayload = null;
        using var client = CreateClient(async request =>
        {
            capturedMethod = request.Method;
            capturedUri = request.RequestUri?.ToString();
            if (request.Headers.TryGetValues("Authorization", out var authValues))
            {
                capturedAuthValues = authValues.ToArray();
            }

            capturedPayload = await ReadJsonAsync(request.Content!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("done", Encoding.UTF8, "text/plain")
            };
        });
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("weather", result.ToolName);
        Assert.Equal("done", result.Output);
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("https://example.com/tools/weather", capturedUri);
        Assert.NotNull(capturedAuthValues);
        Assert.Equal("Bearer token", Assert.Single(capturedAuthValues!));
        Assert.True(capturedPayload.HasValue);

        var payload = capturedPayload.Value;
        Assert.Equal("weather", payload.GetProperty("toolName").GetString());
        Assert.Equal("{\"city\":\"Shanghai\"}", payload.GetProperty("input").GetString());
        Assert.Equal("Shanghai", payload.GetProperty("inputJson").GetProperty("city").GetString());
        Assert.Equal("object", payload.GetProperty("inputSchema").GetProperty("type").GetString());
        Assert.Equal("string", payload.GetProperty("outputSchema").GetProperty("type").GetString());
        Assert.Equal("run-001", payload.GetProperty("context").GetProperty("runId").GetString());
        Assert.Equal("writer", payload.GetProperty("context").GetProperty("agentCode").GetString());
        Assert.Equal("say hi", payload.GetProperty("context").GetProperty("userInput").GetString());
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnCompletedError_WhenResponseIsNotSuccess()
    {
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream error", Encoding.UTF8, "text/plain")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.Equal("Tool webhook failed with HTTP 502: upstream error", result.Output);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnCompletedTimeout_WhenRequestTimesOut()
    {
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("late", Encoding.UTF8, "text/plain")
            };
        });
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(timeoutMs: 10), CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.Equal("Tool webhook timed out after 10ms.", result.Output);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnCompletedRawBody_WhenResponseIsNotStandardJson()
    {
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"message\":\"ok\"}", Encoding.UTF8, "application/json")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.Equal("{\"message\":\"ok\"}", result.Output);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnPending_WhenResponseContainsPendingAndWaitToken()
    {
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"output\":\"working\",\"isPending\":true,\"waitToken\":\"wait-123\"}", Encoding.UTF8, "application/json")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsPending);
        Assert.Equal("wait-123", result.WaitToken);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public async Task InvokeAsync_ShouldGenerateWaitToken_WhenPendingResponseHasNoWaitToken()
    {
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"output\":\"working\",\"isPending\":true}", Encoding.UTF8, "application/json")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsPending);
        Assert.False(string.IsNullOrWhiteSpace(result.WaitToken));
        Assert.Matches("^[a-f0-9]{32}$", result.WaitToken!);
    }

    private HttpToolInvocationRequest CreateRequest(int timeoutMs = 5000)
    {
        return new HttpToolInvocationRequest(
            "weather",
            "https://example.com/tools/weather",
            "POST",
            "{\"Authorization\":\"Bearer token\"}",
            "{\"city\":\"Shanghai\"}",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            _context,
            timeoutMs);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        return CreateClient((request, _) => handler(request));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new StubHttpMessageHandler(handler), disposeHandler: true);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpContent content)
    {
        using var document = JsonDocument.Parse(await content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
