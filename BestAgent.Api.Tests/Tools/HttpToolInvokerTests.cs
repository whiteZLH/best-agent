using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BestAgent.Api.Tests;
using BestAgent.Application.Tools;
using BestAgent.Infrastructure.Tools;
using Microsoft.Extensions.Logging;
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
        string[]? capturedIdempotencyValues = null;
        JsonElement? capturedPayload = null;
        using var client = CreateClient(async request =>
        {
            capturedMethod = request.Method;
            capturedUri = request.RequestUri?.ToString();
            if (request.Headers.TryGetValues("Authorization", out var authValues))
            {
                capturedAuthValues = authValues.ToArray();
            }
            if (request.Headers.TryGetValues("Idempotency-Key", out var idempotencyValues))
            {
                capturedIdempotencyValues = idempotencyValues.ToArray();
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
        Assert.NotNull(capturedIdempotencyValues);
        Assert.Equal("tool-idempotency-key", Assert.Single(capturedIdempotencyValues!));
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
    public async Task InvokeAsync_ShouldRetryRetryableHttpFailure_AndReturnSuccessfulAttempt()
    {
        var attempts = 0;
        var logger = new ListLogger<HttpToolInvoker>();
        using var client = CreateClient(_ =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("upstream error", Encoding.UTF8, "text/plain")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"output\":\"recovered\"}", Encoding.UTF8, "application/json")
                });
        });
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory, logger);

        var result = await invoker.InvokeAsync(CreateRequest(retryPolicy: "{\"maxAttempts\":2,\"delayMs\":0}"), CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.False(result.IsPending);
        Assert.Equal("recovered", result.Output);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("returned HTTP 502 on attempt 1/2; retrying", StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Message.Contains("completed with outcome completed on attempt 2/2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotRetryNonRetryableHttpFailure()
    {
        var attempts = 0;
        using var client = CreateClient(_ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad input", Encoding.UTF8, "text/plain")
            });
        });
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(retryPolicy: "{\"maxAttempts\":3,\"delayMs\":0}"), CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.False(result.IsPending);
        Assert.Equal("Tool webhook failed with HTTP 400: bad input", result.Output);
    }

    [Fact]
    public async Task InvokeAsync_ShouldRetryTimeout_AndReturnSuccessfulAttempt()
    {
        var attempts = 0;
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            attempts++;
            if (attempts == 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"output\":\"after-timeout\"}", Encoding.UTF8, "application/json")
            };
        });
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(timeoutMs: 10, retryPolicy: "retry-once"), CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.False(result.IsPending);
        Assert.Equal("after-timeout", result.Output);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnCompletedTimeout_WhenRequestTimesOut()
    {
        var logger = new ListLogger<HttpToolInvoker>();
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("late", Encoding.UTF8, "text/plain")
            };
        });
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory, logger);

        var result = await invoker.InvokeAsync(CreateRequest(timeoutMs: 10), CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.Equal("Tool webhook timed out after 10ms.", result.Output);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("timed out after 10ms on attempt 1/1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_ShouldLogSanitizedEndpointWithoutSecretsOrPayload()
    {
        var logger = new ListLogger<HttpToolInvoker>();
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"output\":\"done\"}", Encoding.UTF8, "application/json")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory, logger);

        var result = await invoker.InvokeAsync(
            CreateRequestWithEndpoint("https://example.com/tools/weather?token=secret-value&trace=1"),
            CancellationToken.None);

        Assert.Equal("done", result.Output);
        Assert.NotEmpty(logger.Entries);
        Assert.All(
            logger.Entries,
            entry =>
            {
                Assert.DoesNotContain("secret-value", entry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("Bearer token", entry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("Shanghai", entry.Message, StringComparison.Ordinal);
            });
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("https://example.com/tools/weather", StringComparison.Ordinal));
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

    [Fact]
    public async Task InvokeAsync_ShouldReturnCompletedData_WhenResponseUsesStandardSucceededEnvelope()
    {
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"succeeded\",\"data\":{\"temperature\":26.5,\"unit\":\"celsius\"},\"meta\":{\"durationMs\":320}}",
                Encoding.UTF8,
                "application/json")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.Equal("{\"temperature\":26.5,\"unit\":\"celsius\"}", result.Output);
        Assert.Equal("succeeded", result.Status);
        Assert.Null(result.Error);
        Assert.Equal("{\"durationMs\":320}", result.Meta);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnPending_WhenResponseUsesStandardPendingEnvelope()
    {
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"pending\",\"meta\":{\"waitToken\":\"wait-456\"}}",
                Encoding.UTF8,
                "application/json")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsPending);
        Assert.Equal("wait-456", result.WaitToken);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal("pending", result.Status);
        Assert.Null(result.Error);
        Assert.Equal("{\"waitToken\":\"wait-456\"}", result.Meta);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnCompletedError_WhenResponseUsesStandardFailedEnvelope()
    {
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"failed\",\"error\":{\"code\":\"upstream_failed\",\"message\":\"tool backend crashed\"}}",
                Encoding.UTF8,
                "application/json")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.True(result.IsFailed);
        Assert.Equal("{\"code\":\"upstream_failed\",\"message\":\"tool backend crashed\"}", result.Output);
        Assert.Equal("failed", result.Status);
        Assert.Equal("{\"code\":\"upstream_failed\",\"message\":\"tool backend crashed\"}", result.Error);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnFailedResult_WhenLegacyHttpResponseIsNotSuccess()
    {
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream error", Encoding.UTF8, "text/plain")
        }));
        _httpClientFactory.CreateClient("ToolWebhook").Returns(client);
        var invoker = new HttpToolInvoker(_httpClientFactory);

        var result = await invoker.InvokeAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Equal("failed", result.Status);
        Assert.Equal("Tool webhook failed with HTTP 502: upstream error", result.Error);
    }

    private HttpToolInvocationRequest CreateRequest(int timeoutMs = 5000, string? retryPolicy = null)
    {
        return new HttpToolInvocationRequest(
            "weather",
            "https://example.com/tools/weather",
            "POST",
            "{\"Authorization\":\"Bearer token\"}",
            "tool-idempotency-key",
            "{\"city\":\"Shanghai\"}",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            retryPolicy,
            _context,
            timeoutMs);
    }

    private HttpToolInvocationRequest CreateRequestWithEndpoint(string endpointUrl)
    {
        return new HttpToolInvocationRequest(
            "weather",
            endpointUrl,
            "POST",
            "{\"Authorization\":\"Bearer token\"}",
            "tool-idempotency-key",
            "{\"city\":\"Shanghai\"}",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            null,
            _context,
            5000);
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
