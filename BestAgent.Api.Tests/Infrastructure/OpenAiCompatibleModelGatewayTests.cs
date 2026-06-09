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
                      "id": "chatcmpl_123",
                      "service_tier": "flex",
                      "choices": [
                        {
                          "finish_reason": "stop",
                          "message": {
                            "reasoning_summary": [
                              {
                                "text": "Need to answer directly."
                              }
                            ],
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
        Assert.Equal(GenerateTextFinishReasons.Completed, result.FinishReason);
        Assert.Equal("Need to answer directly.", result.ReasoningSummary);
        Assert.Equal("chatcmpl_123", result.ResponseId);
        Assert.Equal("flex", result.ServiceTier);
        Assert.True(capturedPayload.HasValue);
        Assert.Equal(0.2m, capturedPayload.Value.GetProperty("temperature").GetDecimal());
        Assert.True(capturedPayload.Value.TryGetProperty("max_completion_tokens", out var maxTokensElement));
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
        Assert.True(capturedPayload.Value.TryGetProperty("reasoning_effort", out var reasoningEffortElement));
        Assert.Equal(JsonValueKind.Null, reasoningEffortElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("verbosity", out var verbosityElement));
        Assert.Equal(JsonValueKind.Null, verbosityElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("metadata", out var metadataElement));
        Assert.Equal(JsonValueKind.Null, metadataElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("service_tier", out var serviceTierElement));
        Assert.Equal(JsonValueKind.Null, serviceTierElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("store", out var storeElement));
        Assert.Equal(JsonValueKind.Null, storeElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("logprobs", out var logProbsElement));
        Assert.Equal(JsonValueKind.Null, logProbsElement.ValueKind);
        Assert.True(capturedPayload.Value.TryGetProperty("top_logprobs", out var topLogProbsElement));
        Assert.Equal(JsonValueKind.Null, topLogProbsElement.ValueKind);
        var activity = Assert.Single(collector.Activities, value => value.OperationName == AgentTracing.ModelCallActivityName);
        Assert.Equal("gpt-4o-mini", activity.GetTagItem("bestagent.model"));
        Assert.Equal("completed", activity.GetTagItem("bestagent.status"));
        Assert.Equal(18, activity.GetTagItem("bestagent.total_tokens"));
        Assert.Equal(GenerateTextFinishReasons.Completed, activity.GetTagItem("bestagent.finish_reason"));
        Assert.Equal("flex", activity.GetTagItem("bestagent.service_tier"));
        Assert.Equal("chatcmpl_123", activity.GetTagItem("bestagent.response_id"));
        Assert.Equal("Need to answer directly.", activity.GetTagItem("bestagent.reasoning_summary"));
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
        Assert.Equal(256, capturedPayload.Value.GetProperty("max_completion_tokens").GetInt32());
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
    public async Task GenerateTextAsync_ShouldSendUserField_WhenUserIdProvided()
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
                UserId: " user-123 "),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("user-123", capturedPayload.Value.GetProperty("user").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendNormalizedMetadata_WhenProvided()
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
                Metadata: new Dictionary<string, string>
                {
                    [" bestagent.run_id "] = " run-123 ",
                    [" "] = "ignored",
                    ["blank-value"] = " "
                }),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var metadata = capturedPayload.Value.GetProperty("metadata");
        Assert.Equal("run-123", metadata.GetProperty("bestagent.run_id").GetString());
        Assert.Single(metadata.EnumerateObject());
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
    public async Task GenerateTextAsync_ShouldSendMessageName_WhenProvided()
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
                    new GenerateTextMessage("user", "Hello", " customer ")
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var message = Assert.Single(capturedPayload.Value.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("Hello", message.GetProperty("content").GetString());
        Assert.Equal("customer", message.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendDeveloperMessages_WhenProvided()
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
                    new GenerateTextMessage("DEVELOPER", "Follow the policy.")
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var message = Assert.Single(capturedPayload.Value.GetProperty("messages").EnumerateArray());
        Assert.Equal("developer", message.GetProperty("role").GetString());
        Assert.Equal("Follow the policy.", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendStructuredMessageContentParts_WhenProvided()
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
                    new GenerateTextMessage(
                        "user",
                        ContentParts:
                        [
                            new GenerateTextMessageTextPart("Describe this image."),
                            new GenerateTextMessageImageUrlPart(" https://example.com/image.png ", "HIGH"),
                            new GenerateTextMessageInputAudioPart(" base64-audio ", "WAV"),
                            new GenerateTextMessageFilePart(FileId: " file_123 ", FileName: " note.txt ")
                        ])
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var message = Assert.Single(capturedPayload.Value.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        var contentParts = message.GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal(4, contentParts.Length);
        Assert.Equal("text", contentParts[0].GetProperty("type").GetString());
        Assert.Equal("Describe this image.", contentParts[0].GetProperty("text").GetString());
        Assert.Equal("image_url", contentParts[1].GetProperty("type").GetString());
        Assert.Equal("https://example.com/image.png", contentParts[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("high", contentParts[1].GetProperty("image_url").GetProperty("detail").GetString());
        Assert.Equal("input_audio", contentParts[2].GetProperty("type").GetString());
        Assert.Equal("base64-audio", contentParts[2].GetProperty("input_audio").GetProperty("data").GetString());
        Assert.Equal("wav", contentParts[2].GetProperty("input_audio").GetProperty("format").GetString());
        Assert.Equal("file", contentParts[3].GetProperty("type").GetString());
        Assert.Equal("file_123", contentParts[3].GetProperty("file").GetProperty("file_id").GetString());
        Assert.Equal("note.txt", contentParts[3].GetProperty("file").GetProperty("filename").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendAssistantToolCalls_WhenProvided()
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
                    new GenerateTextMessage(
                        "assistant",
                        ToolCalls:
                        [
                            new GenerateTextToolCall("call_123", "function", "weather", "{\"city\":\"Shanghai\"}")
                        ])
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var message = Assert.Single(capturedPayload.Value.GetProperty("messages").EnumerateArray());
        Assert.Equal("assistant", message.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, message.GetProperty("content").ValueKind);
        var toolCall = Assert.Single(message.GetProperty("tool_calls").EnumerateArray());
        Assert.Equal("call_123", toolCall.GetProperty("id").GetString());
        Assert.Equal("function", toolCall.GetProperty("type").GetString());
        Assert.Equal("weather", toolCall.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{\"city\":\"Shanghai\"}", toolCall.GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendToolCallId_ForToolMessages()
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
                    new GenerateTextMessage("assistant", "Calling tool"),
                    new GenerateTextMessage("tool", "{\"temperatureC\":26}", ToolCallId: " call_123 ")
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var messages = capturedPayload.Value.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Equal("tool", messages[1].GetProperty("role").GetString());
        Assert.Equal("{\"temperatureC\":26}", messages[1].GetProperty("content").GetString());
        Assert.Equal("call_123", messages[1].GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectMessagesWithoutContentOrContentParts()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "Ignored system prompt",
                    "Ignored input",
                    Messages:
                    [
                        new GenerateTextMessage("user", " ")
                    ]),
                CancellationToken.None));

        Assert.Equal("Model messages must include at least one valid message.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectToolCallsOnNonAssistantMessages()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "Ignored system prompt",
                    "Ignored input",
                    Messages:
                    [
                        new GenerateTextMessage(
                            "user",
                            ToolCalls:
                            [
                                new GenerateTextToolCall("call_123", "function", "weather", "{\"city\":\"Shanghai\"}")
                            ])
                    ]),
                CancellationToken.None));

        Assert.Equal("Only assistant messages can include tool_calls.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectStructuredContentPartsForToolMessages()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "Ignored system prompt",
                    "Ignored input",
                    Messages:
                    [
                        new GenerateTextMessage(
                            "tool",
                            ToolCallId: "call_123",
                            ContentParts:
                            [
                                new GenerateTextMessageTextPart("tool output")
                            ])
                    ]),
                CancellationToken.None));

        Assert.Equal("Model tool messages must use plain string content.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectUnsupportedMessageRole()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "Ignored system prompt",
                    "Ignored input",
                    Messages:
                    [
                        new GenerateTextMessage("critic", "Nope")
                    ]),
                CancellationToken.None));

        Assert.Equal("Model message role 'critic' is not supported.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectUnsupportedImageDetail()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "Ignored system prompt",
                    "Ignored input",
                    Messages:
                    [
                        new GenerateTextMessage(
                            "user",
                            ContentParts:
                            [
                                new GenerateTextMessageImageUrlPart("https://example.com/image.png", "full")
                            ])
                    ]),
                CancellationToken.None));

        Assert.Equal("Model image detail 'full' is not supported.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectAssistantToolCallsWithInvalidArguments()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "Ignored system prompt",
                    "Ignored input",
                    Messages:
                    [
                        new GenerateTextMessage(
                            "assistant",
                            ToolCalls:
                            [
                                new GenerateTextToolCall("call_123", "function", "weather", "[1,2,3]")
                            ])
                    ]),
                CancellationToken.None));

        Assert.Equal("Model assistant tool call arguments must be a JSON object.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectToolMessagesWithoutToolCallId()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "Ignored system prompt",
                    "Ignored input",
                    Messages:
                    [
                        new GenerateTextMessage("tool", "{\"temperatureC\":26}")
                    ]),
                CancellationToken.None));

        Assert.Equal("Model tool messages must include a tool_call_id.", exception.Message);
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
    public async Task GenerateTextAsync_ShouldPreferRequestSeedOverConfiguredDefault()
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
                Seed = 11
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello", Seed: 42),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(42, capturedPayload.Value.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestLogitBiasOverConfiguredDefault()
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
                LogitBias = new Dictionary<int, int> { [10] = 20 }
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                LogitBias: new Dictionary<int, int>
                {
                    [42] = 120,
                    [7] = -120
                }),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var logitBias = capturedPayload.Value.GetProperty("logit_bias");
        Assert.Equal(100, logitBias.GetProperty("42").GetInt32());
        Assert.Equal(-100, logitBias.GetProperty("7").GetInt32());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestStopSequencesOverConfiguredDefault()
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
                StopSequences = ["\n\n", "<END>"]
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                StopSequences: ["DONE", "STOP", "DONE", " ", "TRIMMED  "]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var stop = capturedPayload.Value.GetProperty("stop").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        Assert.Equal(["DONE", "STOP", "TRIMMED"], stop);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestReasoningEffortOverConfiguredDefault()
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
                ReasoningEffort = GenerateTextReasoningEfforts.Low
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                ReasoningEffort: "HIGH"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("high", capturedPayload.Value.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldUseConfiguredReasoningEffort_WhenRequestDoesNotOverride()
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
                ReasoningEffort = GenerateTextReasoningEfforts.Minimal
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("minimal", capturedPayload.Value.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestVerbosityOverConfiguredDefault()
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
                Verbosity = GenerateTextVerbosityLevels.Low
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                Verbosity: "HIGH"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("high", capturedPayload.Value.GetProperty("verbosity").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldUseConfiguredVerbosity_WhenRequestDoesNotOverride()
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
                Verbosity = GenerateTextVerbosityLevels.Medium
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("medium", capturedPayload.Value.GetProperty("verbosity").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestServiceTierOverConfiguredDefault()
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
                ServiceTier = GenerateTextServiceTiers.Default
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                ServiceTier: "FLEX"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("flex", capturedPayload.Value.GetProperty("service_tier").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldUseConfiguredServiceTier_WhenRequestDoesNotOverride()
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
                ServiceTier = GenerateTextServiceTiers.Priority
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("priority", capturedPayload.Value.GetProperty("service_tier").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestStoreOverConfiguredDefault()
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
                Store = true
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                Store: false),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.False(capturedPayload.Value.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldUseConfiguredStore_WhenRequestDoesNotOverride()
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
                Store = true
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.True(capturedPayload.Value.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestLogProbsOverConfiguredDefault()
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
                LogProbs = false
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                LogProbs: true),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.True(capturedPayload.Value.GetProperty("logprobs").GetBoolean());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldUseConfiguredLogProbs_WhenRequestDoesNotOverride()
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
                LogProbs = true
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.True(capturedPayload.Value.GetProperty("logprobs").GetBoolean());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestTopLogProbsOverConfiguredDefault()
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
                LogProbs = true,
                TopLogProbs = 3
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello",
                LogProbs: true,
                TopLogProbs: 7),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(7, capturedPayload.Value.GetProperty("top_logprobs").GetInt32());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldUseConfiguredTopLogProbs_WhenRequestDoesNotOverride()
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
                LogProbs = true,
                TopLogProbs = 5
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(string.Empty, "You are helpful.", "Hello"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(5, capturedPayload.Value.GetProperty("top_logprobs").GetInt32());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectTopLogProbsWhenLogProbsAreDisabled()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    TopLogProbs: 4),
                CancellationToken.None));

        Assert.Equal("Model top_logprobs requires logprobs to be enabled.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectUnsupportedReasoningEffort()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    ReasoningEffort: "turbo"),
                CancellationToken.None));

        Assert.Contains("reasoning effort 'turbo' is not supported", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectUnsupportedVerbosity()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    Verbosity: "verbose"),
                CancellationToken.None));

        Assert.Contains("verbosity 'verbose' is not supported", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectUnsupportedServiceTier()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    ServiceTier: "scale"),
                CancellationToken.None));

        Assert.Contains("service tier 'scale' is not supported", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestParallelToolCallsOverConfiguredDefault()
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
                ParallelToolCalls = true
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
                ],
                ParallelToolCalls: false),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.False(capturedPayload.Value.GetProperty("parallel_tool_calls").GetBoolean());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldDefaultParallelToolCallsToFalse_WhenToolsAreDeclared()
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
        Assert.False(capturedPayload.Value.GetProperty("parallel_tool_calls").GetBoolean());
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
        Assert.True(jsonSchema.TryGetProperty("description", out var descriptionElement));
        Assert.Equal(JsonValueKind.Null, descriptionElement.ValueKind);
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        Assert.Equal("object", jsonSchema.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("string", jsonSchema.GetProperty("schema").GetProperty("properties").GetProperty("answer").GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldAllowCustomJsonSchemaNameAndStrictMode()
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
                OutputSchema: outputSchema,
                OutputName: "support_answer",
                OutputStrict: false),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var jsonSchema = capturedPayload.Value.GetProperty("response_format").GetProperty("json_schema");
        Assert.Equal("support_answer", jsonSchema.GetProperty("name").GetString());
        Assert.False(jsonSchema.GetProperty("strict").GetBoolean());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldSendJsonSchemaDescription_WhenProvided()
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
                OutputSchema: outputSchema,
                OutputDescription: " Structured support answer "),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var jsonSchema = capturedPayload.Value.GetProperty("response_format").GetProperty("json_schema");
        Assert.Equal("Structured support answer", jsonSchema.GetProperty("description").GetString());
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
    public async Task GenerateTextAsync_ShouldSendTextResponseFormat_WhenRequested()
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
                OutputMode: GenerateTextOutputModes.Text),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var responseFormat = capturedPayload.Value.GetProperty("response_format");
        Assert.Equal("text", responseFormat.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectOutputSchemaOutsideJsonSchemaMode()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    OutputMode: GenerateTextOutputModes.Text,
                    OutputSchema: "{\"type\":\"object\"}"),
                CancellationToken.None));

        Assert.Equal("Model output schema can only be used when output mode is json_schema.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectOutputNameOutsideJsonSchemaMode()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    OutputName: "support_answer"),
                CancellationToken.None));

        Assert.Equal("Model output name can only be used when output mode is json_schema.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectOutputDescriptionOutsideJsonSchemaMode()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    OutputDescription: "Structured support answer"),
                CancellationToken.None));

        Assert.Equal("Model output description can only be used when output mode is json_schema.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectOutputStrictOutsideJsonSchemaMode()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    OutputStrict: false),
                CancellationToken.None));

        Assert.Equal("Model output strict flag can only be used when output mode is json_schema.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectUnnamedToolDefinitions()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    Tools:
                    [
                        new GenerateTextToolDefinition(
                            " ",
                            "Ignored",
                            "{\"type\":\"object\"}")
                    ]),
                CancellationToken.None));

        Assert.Equal("Model tools must include at least one named tool.", exception.Message);
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
                        "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"],\"additionalProperties\":false}",
                        true)
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var tools = capturedPayload.Value.GetProperty("tools");
        var tool = Assert.Single(tools.EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        var function = tool.GetProperty("function");
        Assert.Equal("weather", function.GetProperty("name").GetString());
        Assert.Equal("Get the weather for a city", function.GetProperty("description").GetString());
        Assert.True(function.GetProperty("strict").GetBoolean());
        var parameters = function.GetProperty("parameters");
        Assert.Equal("object", parameters.GetProperty("type").GetString());
        Assert.Equal("string", parameters.GetProperty("properties").GetProperty("city").GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldAllowNonStrictToolDefinitions()
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
                        "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"],\"additionalProperties\":false}",
                        false)
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        var function = capturedPayload.Value.GetProperty("tools")[0].GetProperty("function");
        Assert.False(function.GetProperty("strict").GetBoolean());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectAutoToolChoiceWithoutDeclaredTools()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    ToolChoice: "auto"),
                CancellationToken.None));

        Assert.Equal("Model tool choice 'auto' requires at least one declared tool.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectRequiredToolChoiceWithoutDeclaredTools()
    {
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    ToolChoice: "required"),
                CancellationToken.None));

        Assert.Equal("Model tool choice 'required' requires at least one declared tool.", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldUseConfiguredToolChoice_WhenRequestDoesNotOverride()
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
                ToolChoice = "required"
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
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("required", capturedPayload.Value.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldIgnoreConfiguredToolChoice_WhenNoToolsAreDeclared()
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
                ToolChoice = "required"
            });

        await gateway.GenerateTextAsync(
            new GenerateTextRequest(
                string.Empty,
                "You are helpful.",
                "Hello"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal(JsonValueKind.Null, capturedPayload.Value.GetProperty("tool_choice").ValueKind);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldDefaultToolChoiceToAuto_WhenToolsAreDeclared()
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
                ]),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("auto", capturedPayload.Value.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldPreferRequestToolChoiceOverConfiguredDefault()
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
                ToolChoice = "required"
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
                ToolChoice: "none"),
            CancellationToken.None);

        Assert.True(capturedPayload.HasValue);
        Assert.Equal("none", capturedPayload.Value.GetProperty("tool_choice").GetString());
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

        Assert.Equal(GenerateTextFinishReasons.ToolCall, result.FinishReason);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("tool_call", document.RootElement.GetProperty("action").GetString());
        Assert.Equal("weather", document.RootElement.GetProperty("toolName").GetString());
        Assert.Equal("{\"city\":\"Shanghai\"}", document.RootElement.GetProperty("toolInput").GetString());
        var toolCall = Assert.Single(result.ToolCalls!);
        Assert.Equal("call_123", toolCall.Id);
        Assert.Equal("function", toolCall.Type);
        Assert.Equal("weather", toolCall.Name);
        Assert.Equal("{\"city\":\"Shanghai\"}", toolCall.Arguments);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldNormalizeLengthFinishReason()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "finish_reason": "length",
                          "message": {
                            "content": "{\"action\":\"respond\",\"response\":\"hello\"}"
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

        Assert.Equal(GenerateTextFinishReasons.MaxOutputTokens, result.FinishReason);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldExtractArrayContentSegments()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": [
                              {
                                "type": "text",
                                "text": "hello"
                              },
                              {
                                "type": "text",
                                "text": "world"
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

        Assert.Equal("hello\nworld", result.Output);
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
                new GenerateTextRequest(
                    string.Empty,
                    "You are helpful.",
                    "Hello",
                    Tools:
                    [
                        new GenerateTextToolDefinition(
                            "weather",
                            "Get the weather for a city",
                            "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"],\"additionalProperties\":false}"),
                        new GenerateTextToolDefinition(
                            "calendar",
                            "Create a calendar event",
                            "{\"type\":\"object\",\"properties\":{\"date\":{\"type\":\"string\"}},\"required\":[\"date\"],\"additionalProperties\":false}")
                    ]),
                CancellationToken.None));

        Assert.Contains("only one tool call per turn", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectUndeclaredNativeToolCall()
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
                CancellationToken.None));

        Assert.Contains("undeclared native tool call 'calendar'", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectNativeToolCallWithInvalidArguments()
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
                                  "arguments": "not-json"
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
                CancellationToken.None));

        Assert.Contains("invalid native tool call arguments for 'weather'", exception.Message);
    }

    [Fact]
    public async Task GenerateTextAsync_ShouldRejectNativeToolCallArgumentsThatViolateDeclaredSchema()
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
                                  "arguments": "{}"
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
                CancellationToken.None));

        Assert.Equal("Input for tool 'weather' is missing required property 'city'.", exception.Message);
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
