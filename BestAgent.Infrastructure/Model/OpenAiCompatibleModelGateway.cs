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
                tools
            };
            _logger.LogDebug(
                "Calling model {Model} with timeout {TimeoutSeconds}s, output mode {OutputMode}, tool count {ToolCount}, message count {MessageCount}, temperature {Temperature}, max tokens {MaxOutputTokens}, top_p {TopP}, presence penalty {PresencePenalty}, frequency penalty {FrequencyPenalty}, system prompt length {SystemPromptLength} and input length {InputLength}",
                model,
                timeoutSeconds,
                NormalizeOutputMode(request.OutputMode, request.OutputSchema),
                tools?.Length ?? 0,
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
            var output = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            var finishReason = TryGetFinishReason(document.RootElement);

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
                finishReason);

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
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogInformation(
                "Model call completed for {Model} in {DurationMs}ms with {TotalTokens} total tokens, cost {Cost}, finish reason {FinishReason}",
                model,
                (DateTime.UtcNow - startedAt).TotalMilliseconds,
                result.TotalTokens,
                result.Cost,
                result.FinishReason);

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
}
