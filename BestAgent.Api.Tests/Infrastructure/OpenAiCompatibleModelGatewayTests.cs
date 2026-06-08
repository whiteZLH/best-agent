using System.Net;
using System.Net.Http;
using System.Text;
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
        var activity = Assert.Single(collector.Activities, value => value.OperationName == AgentTracing.ModelCallActivityName);
        Assert.Equal("gpt-4o-mini", activity.GetTagItem("bestagent.model"));
        Assert.Equal("completed", activity.GetTagItem("bestagent.status"));
        Assert.Equal(18, activity.GetTagItem("bestagent.total_tokens"));
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

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
