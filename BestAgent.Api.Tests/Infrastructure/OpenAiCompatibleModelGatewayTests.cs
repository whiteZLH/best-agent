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
                          "finish_reason": "stop",
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
        Assert.Equal("stop", result.FinishReason);
        Assert.True(capturedPayload.HasValue);
        Assert.Equal(0.2m, capturedPayload.Value.GetProperty("temperature").GetDecimal());
        Assert.True(capturedPayload.Value.TryGetProperty("max_tokens", out var maxTokensElement));
        Assert.Equal(JsonValueKind.Null, maxTokensElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("top_p", out var topPElement));
        Assert.Equal(JsonValueKind.Null, topPElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("presence_penalty", out var presencePenaltyElement));
        Assert.Equal(JsonValueKind.Null, presencePenaltyElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("frequency_penalty", out var frequencyPenaltyElement));
        Assert.Equal(JsonValueKind.Null, frequencyPenaltyElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("response_format", out var responseFormatElement));
        Assert.Equal(JsonValueKind.Null, responseFormatElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("tools", out var toolsElement));
        Assert.Equal(JsonValueKind.Null, toolsElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("tool_choice", out var toolChoiceElement));
        Assert.Equal(JsonValueKind.Null, toolChoiceElement.ValueKind);
        var activity = Assert.Single(collector.Activities, value => value.OperationName == AgentTracing.ModelCallActivityName);
        Assert.Equal("gpt-4o-mini", activity.GetTagItem("bestagent.model"));
        Assert.Equal("completed", activity.GetTagItem("bestagent.status"));
        Assert.Equal(18, activity.GetTagItem("bestagent.total_tokens"));
        Assert.Equal("stop", activity.GetTagItem("bestagent.finish_reason"));
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
    public async Task GenerateTextAsync_ShouldPreferRequestTopPOverConfiguredDefault()
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
                TopP = 0.7m
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello", TopP: 0.9m),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(0.9m, capturedPayload.Value.GetProperty("top_p").GetDecimal());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferExplicitMessagesOverSystemPromptAndInput()
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
                Model = "gpt-4o-mini"
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "Ignored system prompt",
                "Ignored input",
                Messages:
                [
                    new GenerateTextMessage("system", "You are helpful."),
                    new GenerateTextMessage("user", "Hello"),
                    new GenerateTextMessage("assistant", "Hi, how can I help?")
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var messages = capturedPayload.Value.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(3, messages.Length);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are helpful.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Hello", messages[1].GetProperty("content").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
        Assert.Equal("Hi, how can I help?", messages[2].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestPresencePenaltyOverConfiguredDefault()
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
                PresencePenalty = 0.2m
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello", PresencePenalty: 1.1m),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(1.1m, capturedPayload.Value.GetProperty("presence_penalty").GetDecimal());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestFrequencyPenaltyOverConfiguredDefault()
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
                FrequencyPenalty = 0.3m
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello", FrequencyPenalty: -1.4m),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(-1.4m, capturedPayload.Value.GetProperty("frequency_penalty").GetDecimal());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldHonorRequestTimeoutOverConfiguredDefault()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
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
            };
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
                TimeoutSeconds = 5
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.GenerateTextAsync(
                new GenerateTextRequest(string.Empty, "You are helpful.", "Hello", TimeoutSeconds: 1),
                CancellationToken.None));

        Assert.Equal("Model gateway timed out after 1s.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldInferJsonSchemaResponseFormat_WhenOutputSchemaProvided()
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
                Model = "gpt-4o-mini"
            });
        const string outputSchema = "{\"type\":\"object\",\"required\":[\"answer\"],\"properties\":{\"answer\":{\"type\":\"string\"}},\"additionalProperties\":false}";

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                OutputSchema: outputSchema),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var responseFormat = capturedPayload.Value.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        var jsonSchema = responseFormat.GetProperty("json_schema");
        Assert.Equal("bestagent_output", jsonSchema.GetProperty("name").GetString());
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        Assert.Equal("object", jsonSchema.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("string", jsonSchema.GetProperty("schema").GetProperty("properties").GetProperty("answer").GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendJsonObjectResponseFormat_WhenRequested()
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
                Model = "gpt-4o-mini"
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                OutputMode: GenerateTextOutputModes.JsonObject),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var responseFormat = capturedPayload.Value.GetProperty("response_format");
        Assert.Equal("json_object", responseFormat.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendTools_WhenRequested()
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
                Model = "gpt-4o-mini"
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                Tools:
                [
                    new GenerateTextToolDefinition(
                        "weather",
                        "Get the weather for a city",
                        "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"],\"additionalProperties\":false}")
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var tools = capturedPayload.Value.GetProperty("tools");
        var tool = Assert.Single(tools.EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        var function = tool.GetProperty("function");
        Assert.Equal("weather", function.GetProperty("name").GetString());
        Assert.Equal("Get the weather for a city", function.GetProperty("description").GetString());
        var parameters = function.GetProperty("parameters");
        Assert.Equal("object", parameters.GetProperty("type").GetString());
        Assert.Equal("string", parameters.GetProperty("properties").GetProperty("city").GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendNamedToolChoice_WhenRequested()
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
                Model = "gpt-4o-mini"
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                Tools:
                [
                    new GenerateTextToolDefinition(
                        "weather",
                        "Get the weather for a city",
                        "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}")
                ],
                ToolChoice: "weather"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var toolChoice = capturedPayload.Value.GetProperty("tool_choice");
        Assert.Equal("function", toolChoice.GetProperty("type").GetString());
        Assert.Equal("weather", toolChoice.GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldNormalizeNativeSingleToolCallResponse()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "finish_reason": "tool_calls",
                          "message": {
                            "content": null,
                            "tool_calls": [
                              {
                                "id": "call_123",
                                "type": "function",
                                "function": {
                                  "name": "weather",
                                  "arguments": "{\"city\":\"Shanghai\"}"
                                }
                              }
                            ]
                          }
                        }
                      ]
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
                Model = "gpt-4o-mini"
            });

        var result = await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
            CancellationToken.None);

        Assert.Equal("tool_calls", result.FinishReason);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("tool_call", document.RootElement.GetProperty("action").GetString());
        Assert.Equal("weather", document.RootElement.GetProperty("toolName").GetString());
        Assert.Equal("{\"city\":\"Shanghai\"}", document.RootElement.GetProperty("toolInput").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectMultipleNativeToolCalls()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "finish_reason": "tool_calls",
                          "message": {
                            "content": null,
                            "tool_calls": [
                              {
                                "id": "call_123",
                                "type": "function",
                                "function": {
                                  "name": "weather",
                                  "arguments": "{\"city\":\"Shanghai\"}"
                                }
                              },
                              {
                                "id": "call_456",
                                "type": "function",
                                "function": {
                                  "name": "calendar",
                                  "arguments": "{\"date\":\"2026-06-10\"}"
                                }
                              }
                            ]
                          }
                        }
                      ]
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
                Model = "gpt-4o-mini"
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.GenerateTextAsync(
                new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
                CancellationToken.None));

        Assert.Contains("only one tool call per turn", exception.Message);
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
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        private readonly Func<HttpRequestMessage, Task>? _beforeResponseAsync;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> handler,
            Func<HttpRequestMessage, Task>? beforeResponseAsync = null)
            : this((request, _) => Task.FromResult(handler(request)), beforeResponseAsync)
        {
        }

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
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

            return await _handler(request, cancellationToken);
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpContent content)
    {
        using var document = JsonDocument.Parse(await content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
