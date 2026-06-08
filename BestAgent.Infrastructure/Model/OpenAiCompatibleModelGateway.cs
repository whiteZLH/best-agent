using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BestAgent.Application.Models;
using BestAgent.Application.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestAgent.Infrastructure.Model;

public class OpenAiCompatibleModelGateway : IModelGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly IAgentMetrics _agentMetrics;
    private readonly ILogger<OpenAiCompatibleModelGateway> _logger;

    public OpenAiCompatibleModelGateway(
        HttpClient httpClient,
        OpenAiOptions options,
        IAgentMetrics? agentMetrics = null,
        ILogger<OpenAiCompatibleModelGateway>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _agentMetrics = agentMetrics ?? NullAgentMetrics.Instance;
        _logger = logger ?? NullLogger<OpenAiCompatibleModelGateway>.Instance;
    }

    public async Task<GenerateTextResult> GenerateTextAsync(GenerateTextRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model;
        using var activity = AgentTracing.Source.StartActivity(AgentTracing.ModelCallActivityName, ActivityKind.Client);
        activity?.SetTag("bestagent.model", string.IsNullOrWhiteSpace(model) ? "unknown" : model.Trim());

        try
        {
            if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            {
                throw new InvalidOperationException("OpenAI:BaseUrl is not configured.");
            }

            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException("No model was provided. Configure OpenAI:Model or AgentDefinitionVersion.DefaultModel.");
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            var timeoutSeconds = NormalizeTimeoutSeconds(request.TimeoutSeconds) ?? NormalizeTimeoutSeconds(_options.TimeoutSeconds) ?? 60;
            var temperature = NormalizeTemperature(request.Temperature ?? _options.Temperature);
            var maxOutputTokens = NormalizeMaxOutputTokens(request.MaxOutputTokens ?? _options.MaxOutputTokens);
            var topP = NormalizeTopP(request.TopP ?? _options.TopP);
            var presencePenalty = NormalizePenalty(request.PresencePenalty ?? _options.PresencePenalty);
            var frequencyPenalty = NormalizePenalty(request.FrequencyPenalty ?? _options.FrequencyPenalty);
            var responseFormat = BuildResponseFormat(request.OutputMode, request.OutputSchema);
            var tools = BuildTools(request.Tools);
            var toolChoice = BuildToolChoice(request.ToolChoice, request.Tools);

            var payload = new
            {
                model,
                messages = BuildMessages(request),
                temperature,
                max_tokens = maxOutputTokens,
                top_p = topP,
                presence_penalty = presencePenalty,
                frequency_penalty = frequencyPenalty,
                response_format = responseFormat,
                tools,
                tool_choice = toolChoice
            };
            _logger.LogDebug(
                "Calling model {Model} with timeout {TimeoutSeconds}s, output mode {OutputMode}, tool count {ToolCount}, tool choice {ToolChoice}, message count {MessageCount}, temperature {Temperature}, max tokens {MaxOutputTokens}, top_p {TopP}, presence penalty {PresencePenalty}, frequency penalty {FrequencyPenalty}, system prompt length {SystemPromptLength} and input length {InputLength}",
                model,
                timeoutSeconds,
                NormalizeOutputMode(request.OutputMode, request.OutputSchema),
                tools?.Length ?? 0,
                NormalizeToolChoice(request.ToolChoice, request.Tools),
                CountMessages(request),
                temperature,
                maxOutputTokens,
                topP,
                presencePenalty,
                frequencyPenalty,
                request.SystemPrompt?.Length ?? 0,
                request.Input?.Length ?? 0);

            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Model gateway returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Trim(body, 512)}");
            }

            using var document = JsonDocument.Parse(body);
            var finishReason = TryGetFinishReason(document.RootElement);
            var reasoningSummary = TryGetReasoningSummary(document.RootElement);
            var toolCalls = TryGetToolCalls(document.RootElement);
            var output = ExtractOutput(document.RootElement, toolCalls);

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException("Model gateway returned an empty response.");
            }

            var promptTokens = TryGetUsageInt(document.RootElement, "prompt_tokens");
            var completionTokens = TryGetUsageInt(document.RootElement, "completion_tokens");
            var totalTokens = TryGetUsageInt(document.RootElement, "total_tokens");
            if (totalTokens <= 0)
            {
                totalTokens = promptTokens + completionTokens;
            }

            var result = new GenerateTextResult(
                output,
                promptTokens,
                completionTokens,
                totalTokens,
                CalculateCost(promptTokens, completionTokens),
                finishReason,
                reasoningSummary,
                toolCalls);

            _agentMetrics.RecordModelCall(
                model,
                "completed",
                DateTime.UtcNow - startedAt,
                result.PromptTokens,
                result.CompletionTokens,
                result.TotalTokens,
                result.Cost);
            activity?.SetTag("bestagent.status", "completed");
            activity?.SetTag("bestagent.prompt_tokens", result.PromptTokens);
            activity?.SetTag("bestagent.completion_tokens", result.CompletionTokens);
            activity?.SetTag("bestagent.total_tokens", result.TotalTokens);
            activity?.SetTag("bestagent.cost", (double)result.Cost);
            if (!string.IsNullOrWhiteSpace(result.FinishReason))
            {
                activity?.SetTag("bestagent.finish_reason", result.FinishReason);
            }
            if (!string.IsNullOrWhiteSpace(result.ReasoningSummary))
            {
                activity?.SetTag("bestagent.reasoning_summary", Trim(result.ReasoningSummary, 512));
            }
            if (result.ToolCalls is { Count: > 0 })
            {
                activity?.SetTag("bestagent.tool_call_count", result.ToolCalls.Count);
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogInformation(
                "Model call completed for {Model} in {DurationMs}ms with {TotalTokens} total tokens, cost {Cost}, finish reason {FinishReason}, reasoning summary length {ReasoningSummaryLength}, native tool call count {ToolCallCount}",
                model,
                (DateTime.UtcNow - startedAt).TotalMilliseconds,
                result.TotalTokens,
                result.Cost,
                result.FinishReason,
                result.ReasoningSummary?.Length ?? 0,
                result.ToolCalls?.Count ?? 0);

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Model gateway timed out after {NormalizeTimeoutSeconds(request.TimeoutSeconds) ?? NormalizeTimeoutSeconds(_options.TimeoutSeconds) ?? 60}s.");
        }
        catch (Exception ex)
        {
            _agentMetrics.RecordModelCall(
                model ?? string.Empty,
                "failed",
                DateTime.UtcNow - startedAt,
                0,
                0,
                0,
                0m);
            activity?.SetTag("bestagent.status", "failed");
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex, "Model call failed for {Model}", model);
            throw;
        }
    }

    private static object[] BuildMessages(GenerateTextRequest request)
    {
        if (request.Messages is { Count: > 0 })
        {
            var messages = request.Messages
                .Where(message =>
                    !string.IsNullOrWhiteSpace(message.Role)
                    && !string.IsNullOrWhiteSpace(message.Content))
                .Select(message => new
                {
                    role = message.Role.Trim(),
                    content = message.Content.Trim()
                })
                .Cast<object>()
                .ToArray();
            if (messages.Length > 0)
            {
                return messages;
            }
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return
            [
                new { role = "user", content = request.Input }
            ];
        }

        return
        [
            new { role = "system", content = request.SystemPrompt },
            new { role = "user", content = request.Input }
        ];
    }

    private static int CountMessages(GenerateTextRequest request)
    {
        if (request.Messages is { Count: > 0 })
        {
            return request.Messages.Count(message =>
                !string.IsNullOrWhiteSpace(message.Role)
                && !string.IsNullOrWhiteSpace(message.Content));
        }

        return string.IsNullOrWhiteSpace(request.SystemPrompt) ? 1 : 2;
    }

    private static string Trim(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private decimal CalculateCost(int promptTokens, int completionTokens)
    {
        if (_options.PromptTokenPricePerMillion <= 0m
            && _options.CompletionTokenPricePerMillion <= 0m)
        {
            return 0m;
        }

        var promptCost = promptTokens <= 0 || _options.PromptTokenPricePerMillion <= 0m
            ? 0m
            : (promptTokens / 1_000_000m) * _options.PromptTokenPricePerMillion;
        var completionCost = completionTokens <= 0 || _options.CompletionTokenPricePerMillion <= 0m
            ? 0m
            : (completionTokens / 1_000_000m) * _options.CompletionTokenPricePerMillion;

        return promptCost + completionCost;
    }

    private static decimal NormalizeTemperature(decimal temperature)
    {
        return temperature switch
        {
            < 0m => 0m,
            > 2m => 2m,
            _ => temperature
        };
    }

    private static int? NormalizeMaxOutputTokens(int? maxOutputTokens)
    {
        if (maxOutputTokens is null || maxOutputTokens <= 0)
        {
            return null;
        }

        return maxOutputTokens;
    }

    private static decimal? NormalizeTopP(decimal? topP)
    {
        if (topP is null || topP <= 0m)
        {
            return null;
        }

        return topP > 1m ? 1m : topP;
    }

    private static decimal? NormalizePenalty(decimal? penalty)
    {
        if (penalty is null)
        {
            return null;
        }

        return penalty.Value switch
        {
            < -2m => -2m,
            > 2m => 2m,
            _ => penalty
        };
    }

    private static int? NormalizeTimeoutSeconds(int? timeoutSeconds)
    {
        if (timeoutSeconds is null || timeoutSeconds <= 0)
        {
            return null;
        }

        return timeoutSeconds;
    }

    private static object? BuildResponseFormat(string? outputMode, string? outputSchema)
    {
        var normalizedOutputMode = NormalizeOutputMode(outputMode, outputSchema);
        return normalizedOutputMode switch
        {
            null or GenerateTextOutputModes.Text => null,
            GenerateTextOutputModes.JsonObject => new
            {
                type = GenerateTextOutputModes.JsonObject
            },
            GenerateTextOutputModes.JsonSchema => new
            {
                type = GenerateTextOutputModes.JsonSchema,
                json_schema = new
                {
                    name = "bestagent_output",
                    strict = true,
                    schema = ParseOutputSchema(outputSchema)
                }
            },
            _ => throw new InvalidOperationException($"Model output mode '{outputMode}' is not supported.")
        };
    }

    private static string? NormalizeOutputMode(string? outputMode, string? outputSchema)
    {
        if (string.IsNullOrWhiteSpace(outputMode))
        {
            return string.IsNullOrWhiteSpace(outputSchema)
                ? null
                : GenerateTextOutputModes.JsonSchema;
        }

        var normalized = outputMode.Trim().ToLowerInvariant();
        return normalized switch
        {
            GenerateTextOutputModes.Text => GenerateTextOutputModes.Text,
            GenerateTextOutputModes.JsonObject => GenerateTextOutputModes.JsonObject,
            GenerateTextOutputModes.JsonSchema => GenerateTextOutputModes.JsonSchema,
            _ => throw new InvalidOperationException($"Model output mode '{outputMode}' is not supported.")
        };
    }

    private static JsonElement ParseOutputSchema(string? outputSchema)
    {
        if (string.IsNullOrWhiteSpace(outputSchema))
        {
            throw new InvalidOperationException("Model output schema must be provided when output mode is json_schema.");
        }

        try
        {
            using var document = JsonDocument.Parse(outputSchema);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Model output schema must be a JSON object.");
            }

            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Model output schema must be valid JSON.", ex);
        }
    }

    private static object[]? BuildTools(IReadOnlyList<GenerateTextToolDefinition>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return null;
        }

        return tools
            .Where(tool => !string.IsNullOrWhiteSpace(tool.Name))
            .Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name.Trim(),
                    description = string.IsNullOrWhiteSpace(tool.Description) ? null : tool.Description.Trim(),
                    parameters = ParseToolParameters(tool)
                }
            })
            .Cast<object>()
            .ToArray();
    }

    private static JsonElement ParseToolParameters(GenerateTextToolDefinition tool)
    {
        if (string.IsNullOrWhiteSpace(tool.InputSchema))
        {
            return JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { }
            });
        }

        try
        {
            using var document = JsonDocument.Parse(tool.InputSchema);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Tool input schema for '{tool.Name}' must be a JSON object.");
            }

            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Tool input schema for '{tool.Name}' must be valid JSON.", ex);
        }
    }

    private static object? BuildToolChoice(string? toolChoice, IReadOnlyList<GenerateTextToolDefinition>? tools)
    {
        var normalizedToolChoice = NormalizeToolChoice(toolChoice, tools);
        if (string.IsNullOrWhiteSpace(normalizedToolChoice))
        {
            return null;
        }

        return normalizedToolChoice switch
        {
            "auto" => "auto",
            "none" => "none",
            "required" => "required",
            _ => new
            {
                type = "function",
                function = new
                {
                    name = normalizedToolChoice
                }
            }
        };
    }

    private static string? NormalizeToolChoice(string? toolChoice, IReadOnlyList<GenerateTextToolDefinition>? tools)
    {
        if (string.IsNullOrWhiteSpace(toolChoice))
        {
            return null;
        }

        var normalized = toolChoice.Trim();
        if (string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        if (string.Equals(normalized, "required", StringComparison.OrdinalIgnoreCase))
        {
            return "required";
        }

        if (tools is null || tools.Count == 0)
        {
            throw new InvalidOperationException("Model tool choice requires at least one declared tool.");
        }

        var matchedTool = tools.FirstOrDefault(tool =>
            string.Equals(tool.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (matchedTool is null || string.IsNullOrWhiteSpace(matchedTool.Name))
        {
            throw new InvalidOperationException($"Model tool choice '{toolChoice}' does not match any declared tool.");
        }

        return matchedTool.Name.Trim();
    }

    private static int TryGetUsageInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty("usage", out var usage)
            || !usage.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => 0
        };
    }

    private static string? TryGetFinishReason(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("finish_reason", out var finishReason))
        {
            return null;
        }

        return finishReason.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(finishReason.GetString())
                ? null
                : finishReason.GetString()!.Trim(),
            _ => null
        };
    }

    private static string? TryGetReasoningSummary(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryCollectReasoningText(message, "reasoning_summary", out var reasoningSummary))
        {
            return reasoningSummary;
        }

        return TryCollectReasoningText(message, "reasoning", out var reasoning)
            ? reasoning
            : null;
    }

    private static IReadOnlyList<GenerateTextToolCall>? TryGetToolCalls(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("tool_calls", out var toolCalls)
            || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var calls = new List<GenerateTextToolCall>();
        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            var type = toolCall.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
            if (!string.Equals(type, "function", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Model gateway returned unsupported native tool call type '{type ?? "unknown"}'.");
            }

            var id = toolCall.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("Model gateway returned a native tool call without an id.");
            }

            if (!toolCall.TryGetProperty("function", out var function)
                || function.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Model gateway returned a native tool call without a function payload.");
            }

            var toolName = function.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new InvalidOperationException("Model gateway returned a native tool call without a function name.");
            }

            var toolArguments = function.TryGetProperty("arguments", out var argumentsElement)
                && argumentsElement.ValueKind == JsonValueKind.String
                ? argumentsElement.GetString()
                : null;

            calls.Add(new GenerateTextToolCall(
                id.Trim(),
                type!.Trim(),
                toolName.Trim(),
                string.IsNullOrWhiteSpace(toolArguments) ? null : toolArguments.Trim()));
        }

        return calls.Count == 0 ? null : calls;
    }

    private static string? ExtractOutput(JsonElement root, IReadOnlyList<GenerateTextToolCall>? toolCalls)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryBuildNativeToolCallDecision(toolCalls, out var toolCallDecision))
        {
            return toolCallDecision;
        }

        return message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String
            ? content.GetString()
            : null;
    }

    private static bool TryBuildNativeToolCallDecision(
        IReadOnlyList<GenerateTextToolCall>? toolCalls,
        out string? toolCallDecision)
    {
        toolCallDecision = null;
        if (toolCalls is null || toolCalls.Count == 0)
        {
            return false;
        }

        if (toolCalls.Count > 1)
        {
            throw new InvalidOperationException("Model gateway returned multiple native tool calls, but the runtime currently supports only one tool call per turn.");
        }

        var toolCall = toolCalls[0];

        toolCallDecision = JsonSerializer.Serialize(new
        {
            action = "tool_call",
            toolName = toolCall.Name,
            toolInput = toolCall.Arguments
        });
        return true;
    }

    private static bool TryCollectReasoningText(JsonElement parent, string propertyName, out string? reasoningText)
    {
        reasoningText = null;
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        var segments = new List<string>();
        CollectReasoningSegments(value, segments);
        if (segments.Count == 0)
        {
            return false;
        }

        reasoningText = string.Join("\n", segments);
        return true;
    }

    private static void CollectReasoningSegments(JsonElement value, List<string> segments)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(text.Trim());
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    CollectReasoningSegments(item, segments);
                }

                break;
            case JsonValueKind.Object:
                if (value.TryGetProperty("text", out var textProperty))
                {
                    CollectReasoningSegments(textProperty, segments);
                }

                if (value.TryGetProperty("summary", out var summaryProperty))
                {
                    CollectReasoningSegments(summaryProperty, segments);
                }

                if (value.TryGetProperty("content", out var contentProperty))
                {
                    CollectReasoningSegments(contentProperty, segments);
                }

                break;
        }
    }
}
