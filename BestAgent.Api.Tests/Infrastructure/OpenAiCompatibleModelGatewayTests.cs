using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BestAgent.Api.Tests.Observability;
using BestAgent.Application.Models;
using BestAgent.Application.Observability;
using BestAgent.Infrastructure.Model;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class OpenAiCompatibleModelGatewayTests
{
    [Fact]
    public async Task GenerateTextAsync_ShouldEmitCompletedModelCallActivity()
    {
        using var collector = new ActivityTestCollector(AgentTracing.SourceName);
        JsonElement? capturedPayload = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "{\"action\":\"respond\",\"response\":\"hello\"}"
                          }
                        }
                      ],
                      "usage": {
                        "prompt_tokens": 11,
                        "completion_tokens": 7,
                        "total_tokens": 18
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            },
            async request =>
            {
                capturedPayload = await ReadJsonAsync(request.Content!);
            }))
        {
            BaseAddress = new Uri("https://example.com/v1/")
        };
        var gateway = new OpenAiCompatibleModelGateway(
            httpClient,
            new OpenAiOptions
            {
                BaseUrl = "https://example.com/v1/",
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                PromptTokenPricePerMillion = 1m,
                CompletionTokenPricePerMillion = 2m
            });

        var result = await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
            CancellationToken.None);

        Assert.Equal("{\"action\":\"respond\",\"response\":\"hello\"}", result.Output);
        Assert.True(capturedPayload.HasValue);
        Assert.Equal(0.2m, capturedPayload.Value.GetProperty("temperature").GetDecimal());
        Assert.True(capturedPayload.Value.TryGetProperty("max_tokens", out var maxTokensElement));
        Assert.Equal(JsonValueKind.Null, maxTokensElement.ValueKind);
        var activity = Assert.Single(collector.Activities, value => value.OperationName == AgentTracing.ModelCallActivityName);
        Assert.Equal("gpt-4o-mini", activity.GetTagItem("bestagent.model"));
        Assert.Equal("completed", activity.GetTagItem("bestagent.status"));
        Assert.Equal(18, activity.GetTagItem("bestagent.total_tokens"));
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestTemperatureOverConfiguredDefault()
    {
        JsonElement? capturedPayload = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "{\"action\":\"respond\",\"response\":\"hello\"}"
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            },
            async request =>
            {
                capturedPayload = await ReadJsonAsync(request.Content!);
            }))
        {
            BaseAddress = new Uri("https://example.com/v1/")
        };
        var gateway = new OpenAiCompatibleModelGateway(
            httpClient,
            new OpenAiOptions
            {
                BaseUrl = "https://example.com/v1/",
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                Temperature = 0.1m
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello", 1.4m),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(1.4m, capturedPayload.Value.GetProperty("temperature").GetDecimal());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestMaxOutputTokensOverConfiguredDefault()
    {
        JsonElement? capturedPayload = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "{\"action\":\"respond\",\"response\":\"hello\"}"
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            },
            async request =>
            {
                capturedPayload = await ReadJsonAsync(request.Content!);
            }))
        {
            BaseAddress = new Uri("https://example.com/v1/")
        };
        var gateway = new OpenAiCompatibleModelGateway(
            httpClient,
            new OpenAiOptions
            {
                BaseUrl = "https://example.com/v1/",
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                MaxOutputTokens = 128
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello", MaxOutputTokens: 256),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(256, capturedPayload.Value.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldEmitFailedModelCallActivity_WhenGatewayFails()
    {
        using var collector = new ActivityTestCollector(AgentTracing.SourceName);
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom", Encoding.UTF8, "text/plain")
            }))
        {
            BaseAddress = new Uri("https://example.com/v1/")
        };
        var gateway = new OpenAiCompatibleModelGateway(
            httpClient,
            new OpenAiOptions
            {
                BaseUrl = "https://example.com/v1/",
                ApiKey = "test-key",
                Model = "gpt-4o-mini"
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.GenerateTextAsync(
                new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
                CancellationToken.None));

        Assert.Contains("Model gateway returned 500", exception.Message);
        var activity = Assert.Single(collector.Activities, value => value.OperationName == AgentTracing.ModelCallActivityName);
        Assert.Equal("failed", activity.GetTagItem("bestagent.status"));
        Assert.Equal("InvalidOperationException", activity.GetTagItem("error.type"));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        private readonly Func<HttpRequestMessage, Task>? _beforeResponseAsync;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> handler,
            Func<HttpRequestMessage, Task>? beforeResponseAsync = null)
        {
            _handler = handler;
            _beforeResponseAsync = beforeResponseAsync;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_beforeResponseAsync is not null)
            {
                await _beforeResponseAsync(request);
            }

            return _handler(request);
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpContent content)
    {
        using var document = JsonDocument.Parse(await content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
