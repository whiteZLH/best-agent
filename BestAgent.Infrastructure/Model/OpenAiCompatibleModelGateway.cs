using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BestAgent.Application.Models;
using BestAgent.Application.Observability;
using BestAgent.Infrastructure.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestAgent.Infrastructure.Model;

public class OpenAiCompatibleModelGateway : IModelGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex OpenAiCompatibleNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SupportedMessageRoles = new(StringComparer.Ordinal)
    {
        "developer",
        "system",
        "user",
        "assistant",
        "tool"
    };

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
            var logitBias = ResolveLogitBias(request.LogitBias, _options.LogitBias);
            var seed = NormalizeSeed(request.Seed ?? _options.Seed);
            var stopSequences = ResolveStopSequences(request.StopSequences, _options.StopSequences);
            var hasNamedTools = HasNamedTools(request.Tools);
            var parallelToolCalls = NormalizeParallelToolCalls(
                request.ParallelToolCalls
                ?? _options.ParallelToolCalls
                ?? (hasNamedTools ? false : null),
                request.Tools);
            var reasoningEffort = NormalizeReasoningEffort(CoalesceMeaningfulString(request.ReasoningEffort, _options.ReasoningEffort));
            var userId = NormalizeUserId(request.UserId);
            var verbosity = NormalizeVerbosity(CoalesceMeaningfulString(request.Verbosity, _options.Verbosity));
            var metadata = NormalizeMetadata(request.Metadata);
            var serviceTier = NormalizeServiceTier(CoalesceMeaningfulString(request.ServiceTier, _options.ServiceTier));
            var store = request.Store ?? _options.Store;
            var logProbs = request.LogProbs ?? _options.LogProbs;
            var topLogProbs = ResolveTopLogProbs(request.TopLogProbs, _options.TopLogProbs, request.LogProbs, logProbs);
            var responseFormat = BuildResponseFormat(
                request.OutputMode,
                request.OutputSchema,
                request.OutputName,
                request.OutputDescription,
                request.OutputStrict);
            var tools = BuildTools(request.Tools);
            var toolChoiceValue = CoalesceMeaningfulString(request.ToolChoice, null)
                ?? (hasNamedTools ? _options.ToolChoice ?? "auto" : null);
            var toolChoice = BuildToolChoice(toolChoiceValue, request.Tools);

            var payload = new
            {
                model,
                messages = BuildMessages(request),
                temperature,
                max_completion_tokens = maxOutputTokens,
                top_p = topP,
                presence_penalty = presencePenalty,
                frequency_penalty = frequencyPenalty,
                logit_bias = logitBias,
                seed,
                stop = stopSequences,
                parallel_tool_calls = parallelToolCalls,
                reasoning_effort = reasoningEffort,
                user = userId,
                verbosity,
                metadata,
                service_tier = serviceTier,
                store,
                logprobs = logProbs,
                top_logprobs = topLogProbs,
                response_format = responseFormat,
                tools,
                tool_choice = toolChoice
            };
            _logger.LogDebug(
                "Calling model {Model} with timeout {TimeoutSeconds}s, output mode {OutputMode}, tool count {ToolCount}, tool choice {ToolChoice}, message count {MessageCount}, temperature {Temperature}, max completion tokens {MaxOutputTokens}, top_p {TopP}, presence penalty {PresencePenalty}, frequency penalty {FrequencyPenalty}, logit bias count {LogitBiasCount}, seed {Seed}, stop sequence count {StopSequenceCount}, parallel tool calls {ParallelToolCalls}, reasoning effort {ReasoningEffort}, verbosity {Verbosity}, service tier {ServiceTier}, store {Store}, logprobs {LogProbs}, top_logprobs {TopLogProbs}, metadata count {MetadataCount}, user id present {HasUserId}, system prompt length {SystemPromptLength} and input length {InputLength}",
                model,
                timeoutSeconds,
                NormalizeOutputMode(request.OutputMode, request.OutputSchema),
                tools?.Length ?? 0,
                NormalizeToolChoice(toolChoiceValue, request.Tools),
                CountMessages(request),
                temperature,
                maxOutputTokens,
                topP,
                presencePenalty,
                frequencyPenalty,
                logitBias?.Count ?? 0,
                seed,
                stopSequences?.Length ?? 0,
                parallelToolCalls,
                reasoningEffort,
                verbosity,
                serviceTier,
                store,
                logProbs,
                topLogProbs,
                metadata?.Count ?? 0,
                !string.IsNullOrWhiteSpace(userId),
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
                var errorMessage = TryExtractErrorMessage(body);
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? $"Model gateway returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Trim(body, 512)}"
                        : $"Model gateway returned {(int)response.StatusCode} {response.ReasonPhrase}. Error: {Trim(errorMessage, 512)}");
            }

            using var document = JsonDocument.Parse(body);
            var responseId = TryGetResponseId(document.RootElement);
            var finishReason = TryGetFinishReason(document.RootElement);
            var responseServiceTier = TryGetResponseServiceTier(document.RootElement);
            var reasoningSummary = TryGetReasoningSummary(document.RootElement);
            var toolCalls = TryGetToolCalls(document.RootElement, request.Tools);
            var output = ExtractOutput(document.RootElement, toolCalls);

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException("Model gateway returned an empty response.");
            }

            var promptTokens = TryGetUsageInt(document.RootElement, "prompt_tokens", "input_tokens", "promptTokens", "inputTokens");
            var completionTokens = TryGetUsageInt(document.RootElement, "completion_tokens", "output_tokens", "completionTokens", "outputTokens");
            var totalTokens = TryGetUsageInt(document.RootElement, "total_tokens", "totalTokens");
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
                toolCalls,
                responseId,
                responseServiceTier);

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
            if (!string.IsNullOrWhiteSpace(result.ServiceTier))
            {
                activity?.SetTag("bestagent.service_tier", result.ServiceTier);
            }
            if (!string.IsNullOrWhiteSpace(result.ResponseId))
            {
                activity?.SetTag("bestagent.response_id", result.ResponseId);
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
                "Model call completed for {Model} in {DurationMs}ms with {TotalTokens} total tokens, cost {Cost}, finish reason {FinishReason}, service tier {ServiceTier}, response id {ResponseId}, reasoning summary length {ReasoningSummaryLength}, native tool call count {ToolCallCount}",
                model,
                (DateTime.UtcNow - startedAt).TotalMilliseconds,
                result.TotalTokens,
                result.Cost,
                result.FinishReason,
                result.ServiceTier,
                result.ResponseId,
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
        if (request.Messages is not null)
        {
            if (request.Messages.Count == 0)
            {
                throw new InvalidOperationException("Model messages must include at least one message.");
            }

            var messages = request.Messages
                .Select(BuildMessagePayload)
                .Cast<object>()
                .ToArray();
            return messages;
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
        if (request.Messages is not null)
        {
            return request.Messages.Count;
        }

        return string.IsNullOrWhiteSpace(request.SystemPrompt) ? 1 : 2;
    }

    private static object BuildMessagePayload(GenerateTextMessage message)
    {
        var role = NormalizeMessageRole(message.Role);
        var toolCalls = BuildMessageToolCalls(message, role);
        var content = BuildMessageContent(message, toolCalls);
        var name = string.IsNullOrWhiteSpace(message.Name)
            ? null
            : message.Name.Trim();
        var toolCallId = string.IsNullOrWhiteSpace(message.ToolCallId)
            ? null
            : message.ToolCallId.Trim();

        if (role == "tool" && content is not string)
        {
            throw new InvalidOperationException("Model tool messages must use plain string content.");
        }

        if (role == "tool" && string.IsNullOrWhiteSpace(toolCallId))
        {
            throw new InvalidOperationException("Model tool messages must include a tool_call_id.");
        }

        return new
        {
            role,
            content,
            name,
            tool_call_id = toolCallId,
            tool_calls = toolCalls
        };
    }

    private static object? BuildMessageContent(
        GenerateTextMessage message,
        object[]? toolCalls)
    {
        if (message.ContentParts is { Count: > 0 })
        {
            return message.ContentParts
                .Select(BuildMessageContentPart)
                .Cast<object>()
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            return message.Content.Trim();
        }

        if (toolCalls is { Length: > 0 })
        {
            return null;
        }

        throw new InvalidOperationException("Model messages must include content, content parts, or tool calls.");
    }

    private static object[]? BuildMessageToolCalls(
        GenerateTextMessage message,
        string role)
    {
        if (message.ToolCalls is not { Count: > 0 })
        {
            return null;
        }

        if (role != "assistant")
        {
            throw new InvalidOperationException("Only assistant messages can include tool_calls.");
        }

        return message.ToolCalls
            .Select(BuildMessageToolCallPayload)
            .Cast<object>()
            .ToArray();
    }

    private static object BuildMessageToolCallPayload(GenerateTextToolCall toolCall)
    {
        if (string.IsNullOrWhiteSpace(toolCall.Id))
        {
            throw new InvalidOperationException("Model assistant tool calls must include an id.");
        }

        if (string.IsNullOrWhiteSpace(toolCall.Name))
        {
            throw new InvalidOperationException("Model assistant tool calls must include a function name.");
        }

        var type = string.IsNullOrWhiteSpace(toolCall.Type)
            ? "function"
            : toolCall.Type.Trim().ToLowerInvariant();
        if (type != "function")
        {
            throw new InvalidOperationException($"Model assistant tool call type '{toolCall.Type}' is not supported.");
        }

        var arguments = NormalizeMessageToolCallArguments(toolCall.Arguments);

        return new
        {
            id = toolCall.Id.Trim(),
            type,
            function = new
            {
                name = NormalizeCompatibleName(toolCall.Name.Trim(), $"Model assistant tool call name '{toolCall.Name.Trim()}'"),
                arguments
            }
        };
    }

    private static string NormalizeMessageToolCallArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Model assistant tool call arguments must be a JSON object.");
            }

            return arguments.Trim();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Model assistant tool call arguments must be valid JSON.", ex);
        }
    }

    private static object BuildMessageContentPart(GenerateTextMessageContentPart part)
    {
        return part switch
        {
            GenerateTextMessageTextPart textPart => BuildTextContentPart(textPart),
            GenerateTextMessageImageUrlPart imageUrlPart => BuildImageUrlContentPart(imageUrlPart),
            GenerateTextMessageInputAudioPart inputAudioPart => BuildInputAudioContentPart(inputAudioPart),
            GenerateTextMessageFilePart filePart => BuildFileContentPart(filePart),
            _ => throw new InvalidOperationException($"Model message content part type '{part.Type}' is not supported.")
        };
    }

    private static object BuildTextContentPart(GenerateTextMessageTextPart part)
    {
        if (string.IsNullOrWhiteSpace(part.Text))
        {
            throw new InvalidOperationException("Model text content parts must include text.");
        }

        return new
        {
            type = "text",
            text = part.Text.Trim()
        };
    }

    private static object BuildImageUrlContentPart(GenerateTextMessageImageUrlPart part)
    {
        if (string.IsNullOrWhiteSpace(part.Url))
        {
            throw new InvalidOperationException("Model image_url content parts must include a url.");
        }

        var detail = NormalizeImageDetail(part.Detail);
        return new
        {
            type = "image_url",
            image_url = new
            {
                url = part.Url.Trim(),
                detail
            }
        };
    }

    private static string? NormalizeImageDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        return detail.Trim().ToLowerInvariant() switch
        {
            "auto" => "auto",
            "low" => "low",
            "high" => "high",
            _ => throw new InvalidOperationException($"Model image detail '{detail}' is not supported.")
        };
    }

    private static object BuildInputAudioContentPart(GenerateTextMessageInputAudioPart part)
    {
        if (string.IsNullOrWhiteSpace(part.Data))
        {
            throw new InvalidOperationException("Model input_audio content parts must include data.");
        }

        if (string.IsNullOrWhiteSpace(part.Format))
        {
            throw new InvalidOperationException("Model input_audio content parts must include a format.");
        }

        return new
        {
            type = "input_audio",
            input_audio = new
            {
                data = part.Data.Trim(),
                format = part.Format.Trim().ToLowerInvariant()
            }
        };
    }

    private static object BuildFileContentPart(GenerateTextMessageFilePart part)
    {
        var fileId = string.IsNullOrWhiteSpace(part.FileId)
            ? null
            : part.FileId.Trim();
        var fileData = string.IsNullOrWhiteSpace(part.FileData)
            ? null
            : part.FileData.Trim();
        var fileName = string.IsNullOrWhiteSpace(part.FileName)
            ? null
            : part.FileName.Trim();

        if (string.IsNullOrWhiteSpace(fileId) && string.IsNullOrWhiteSpace(fileData))
        {
            throw new InvalidOperationException("Model file content parts must include file_id or file_data.");
        }

        return new
        {
            type = "file",
            file = new
            {
                file_id = fileId,
                file_data = fileData,
                filename = fileName
            }
        };
    }

    private static string NormalizeMessageRole(string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        if (!SupportedMessageRoles.Contains(normalized))
        {
            throw new InvalidOperationException($"Model message role '{role}' is not supported.");
        }

        return normalized;
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

    private static int? NormalizeSeed(int? seed)
    {
        return seed is > 0 ? seed : null;
    }

    private static IReadOnlyDictionary<int, int>? NormalizeLogitBias(IReadOnlyDictionary<int, int>? logitBias)
    {
        if (logitBias is null || logitBias.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<int, int>();
        foreach (var pair in logitBias)
        {
            if (pair.Key < 0)
            {
                continue;
            }

            var bias = pair.Value switch
            {
                < -100 => -100,
                > 100 => 100,
                _ => pair.Value
            };
            normalized[pair.Key] = bias;
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static IReadOnlyDictionary<int, int>? ResolveLogitBias(
        IReadOnlyDictionary<int, int>? requestLogitBias,
        IReadOnlyDictionary<int, int>? configuredLogitBias)
    {
        var normalizedRequest = NormalizeLogitBias(requestLogitBias);
        return normalizedRequest ?? NormalizeLogitBias(configuredLogitBias);
    }

    private static string? CoalesceMeaningfulString(string? value, string? fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int? NormalizeTimeoutSeconds(int? timeoutSeconds)
    {
        if (timeoutSeconds is null || timeoutSeconds <= 0)
        {
            return null;
        }

        return timeoutSeconds;
    }

    private static string[]? NormalizeStopSequences(IReadOnlyList<string>? stopSequences)
    {
        if (stopSequences is null || stopSequences.Count == 0)
        {
            return null;
        }

        var normalized = stopSequences
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();

        return normalized.Length == 0 ? null : normalized;
    }

    private static string[]? ResolveStopSequences(
        IReadOnlyList<string>? requestStopSequences,
        IReadOnlyList<string>? configuredStopSequences)
    {
        var normalizedRequest = NormalizeStopSequences(requestStopSequences);
        return normalizedRequest ?? NormalizeStopSequences(configuredStopSequences);
    }

    private static bool? NormalizeParallelToolCalls(
        bool? parallelToolCalls,
        IReadOnlyList<GenerateTextToolDefinition>? tools)
    {
        return HasNamedTools(tools)
            ? parallelToolCalls
            : null;
    }

    private static string? NormalizeReasoningEffort(string? reasoningEffort)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort))
        {
            return null;
        }

        return reasoningEffort.Trim().ToLowerInvariant() switch
        {
            GenerateTextReasoningEfforts.None => GenerateTextReasoningEfforts.None,
            GenerateTextReasoningEfforts.Minimal => GenerateTextReasoningEfforts.Minimal,
            GenerateTextReasoningEfforts.Low => GenerateTextReasoningEfforts.Low,
            GenerateTextReasoningEfforts.Medium => GenerateTextReasoningEfforts.Medium,
            GenerateTextReasoningEfforts.High => GenerateTextReasoningEfforts.High,
            GenerateTextReasoningEfforts.XHigh => GenerateTextReasoningEfforts.XHigh,
            _ => throw new InvalidOperationException($"Model reasoning effort '{reasoningEffort}' is not supported.")
        };
    }

    private static string? NormalizeUserId(string? userId)
    {
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : userId.Trim();
    }

    private static string? NormalizeVerbosity(string? verbosity)
    {
        if (string.IsNullOrWhiteSpace(verbosity))
        {
            return null;
        }

        return verbosity.Trim().ToLowerInvariant() switch
        {
            GenerateTextVerbosityLevels.Low => GenerateTextVerbosityLevels.Low,
            GenerateTextVerbosityLevels.Medium => GenerateTextVerbosityLevels.Medium,
            GenerateTextVerbosityLevels.High => GenerateTextVerbosityLevels.High,
            _ => throw new InvalidOperationException($"Model verbosity '{verbosity}' is not supported.")
        };
    }

    private static int? NormalizeTopLogProbs(int? topLogProbs, bool? logProbs)
    {
        if (topLogProbs is null)
        {
            return null;
        }

        if (logProbs != true)
        {
            throw new InvalidOperationException("Model top_logprobs requires logprobs to be enabled.");
        }

        return topLogProbs.Value switch
        {
            < 0 => 0,
            > 20 => 20,
            _ => topLogProbs
        };
    }

    private static int? ResolveTopLogProbs(
        int? requestTopLogProbs,
        int? configuredTopLogProbs,
        bool? requestLogProbs,
        bool? effectiveLogProbs)
    {
        if (requestLogProbs == false && requestTopLogProbs is null)
        {
            return null;
        }

        return NormalizeTopLogProbs(requestTopLogProbs ?? configuredTopLogProbs, effectiveLogProbs);
    }

    private static IReadOnlyDictionary<string, string>? NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in metadata)
        {
            if (normalized.Count >= 16)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(pair.Key)
                || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            var key = pair.Key.Trim();
            var value = pair.Value.Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                continue;
            }

            if (key.Length > 64)
            {
                key = key[..64];
            }

            if (value.Length > 512)
            {
                value = value[..512];
            }

            normalized[key] = value;
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static string? NormalizeServiceTier(string? serviceTier)
    {
        if (string.IsNullOrWhiteSpace(serviceTier))
        {
            return null;
        }

        return serviceTier.Trim().ToLowerInvariant() switch
        {
            GenerateTextServiceTiers.Auto => GenerateTextServiceTiers.Auto,
            GenerateTextServiceTiers.Default => GenerateTextServiceTiers.Default,
            GenerateTextServiceTiers.Flex => GenerateTextServiceTiers.Flex,
            GenerateTextServiceTiers.Priority => GenerateTextServiceTiers.Priority,
            _ => throw new InvalidOperationException($"Model service tier '{serviceTier}' is not supported.")
        };
    }

    private static object? BuildResponseFormat(
        string? outputMode,
        string? outputSchema,
        string? outputName,
        string? outputDescription,
        bool? outputStrict)
    {
        var normalizedOutputMode = NormalizeOutputMode(outputMode, outputSchema);
        ValidateResponseFormatConfiguration(
            normalizedOutputMode,
            outputSchema,
            outputName,
            outputDescription,
            outputStrict);

        return normalizedOutputMode switch
        {
            null => null,
            GenerateTextOutputModes.Text => new
            {
                type = GenerateTextOutputModes.Text
            },
            GenerateTextOutputModes.JsonObject => new
            {
                type = GenerateTextOutputModes.JsonObject
            },
            GenerateTextOutputModes.JsonSchema => new
            {
                type = GenerateTextOutputModes.JsonSchema,
                json_schema = new
                {
                    name = NormalizeOutputName(outputName),
                    description = NormalizeOutputDescription(outputDescription),
                    strict = outputStrict ?? true,
                    schema = ParseOutputSchema(outputSchema)
                }
            },
            _ => throw new InvalidOperationException($"Model output mode '{outputMode}' is not supported.")
        };
    }

    private static void ValidateResponseFormatConfiguration(
        string? normalizedOutputMode,
        string? outputSchema,
        string? outputName,
        string? outputDescription,
        bool? outputStrict)
    {
        if (string.Equals(normalizedOutputMode, GenerateTextOutputModes.JsonSchema, StringComparison.Ordinal))
        {
            return;
        }

        if (outputSchema is not null)
        {
            throw new InvalidOperationException("Model output schema can only be used when output mode is json_schema.");
        }

        if (!string.IsNullOrWhiteSpace(outputName))
        {
            throw new InvalidOperationException("Model output name can only be used when output mode is json_schema.");
        }

        if (!string.IsNullOrWhiteSpace(outputDescription))
        {
            throw new InvalidOperationException("Model output description can only be used when output mode is json_schema.");
        }

        if (outputStrict.HasValue)
        {
            throw new InvalidOperationException("Model output strict flag can only be used when output mode is json_schema.");
        }
    }

    private static string? NormalizeOutputMode(string? outputMode, string? outputSchema)
    {
        if (outputMode is null)
        {
            return outputSchema is null
                ? null
                : GenerateTextOutputModes.JsonSchema;
        }

        if (string.IsNullOrWhiteSpace(outputMode))
        {
            throw new InvalidOperationException("Model output mode must not be blank when provided.");
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

    private static string NormalizeOutputName(string? outputName)
    {
        if (outputName is null)
        {
            return "bestagent_output";
        }

        if (string.IsNullOrWhiteSpace(outputName))
        {
            throw new InvalidOperationException("Model output name must not be blank when provided.");
        }

        return NormalizeCompatibleName(outputName.Trim(), "Model output name");
    }

    private static string? NormalizeOutputDescription(string? outputDescription)
    {
        if (outputDescription is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(outputDescription))
        {
            throw new InvalidOperationException("Model output description must not be blank when provided.");
        }

        return outputDescription.Trim();
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
        if (tools is null)
        {
            return null;
        }

        if (tools.Count == 0)
        {
            throw new InvalidOperationException("Model tools must include at least one named tool.");
        }

        var namedTools = GetNamedTools(tools);
        if (namedTools.Count == 0)
        {
            throw new InvalidOperationException("Model tools must include at least one named tool.");
        }

        return namedTools
            .Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = NormalizeToolName(tool.Name),
                    description = string.IsNullOrWhiteSpace(tool.Description) ? null : tool.Description.Trim(),
                    parameters = ParseToolParameters(tool),
                    strict = tool.Strict ?? true
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
            if (!HasNamedTools(tools))
            {
                throw new InvalidOperationException("Model tool choice 'auto' requires at least one declared tool.");
            }

            return "auto";
        }

        if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        if (string.Equals(normalized, "required", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasNamedTools(tools))
            {
                throw new InvalidOperationException("Model tool choice 'required' requires at least one declared tool.");
            }

            return "required";
        }

        var namedTools = GetNamedTools(tools);
        if (namedTools.Count == 0)
        {
            throw new InvalidOperationException("Model tool choice requires at least one declared tool.");
        }

        var matchedTool = namedTools.FirstOrDefault(tool =>
            string.Equals(tool.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (matchedTool is null || string.IsNullOrWhiteSpace(matchedTool.Name))
        {
            throw new InvalidOperationException($"Model tool choice '{toolChoice}' does not match any declared tool.");
        }

        return matchedTool.Name.Trim();
    }

    private static bool HasNamedTools(IReadOnlyList<GenerateTextToolDefinition>? tools)
    {
        return tools is { Count: > 0 } && tools.Any(tool => !string.IsNullOrWhiteSpace(tool.Name));
    }

    private static IReadOnlyList<GenerateTextToolDefinition> GetNamedTools(IReadOnlyList<GenerateTextToolDefinition>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return [];
        }

        var namedTools = tools
            .Where(tool => !string.IsNullOrWhiteSpace(tool.Name))
            .ToArray();
        var duplicateName = namedTools
            .Select(tool => NormalizeToolName(tool.Name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?
            .Key;
        if (!string.IsNullOrWhiteSpace(duplicateName))
        {
            throw new InvalidOperationException($"Model tools must not contain duplicate name '{duplicateName}'.");
        }

        return namedTools;
    }

    private static string NormalizeToolName(string toolName)
    {
        return NormalizeCompatibleName(toolName.Trim(), $"Model tool name '{toolName.Trim()}'");
    }

    private static string NormalizeCompatibleName(string value, string fieldName)
    {
        if (value.Length > 64)
        {
            throw new InvalidOperationException($"{fieldName} must be 64 characters or fewer.");
        }

        if (!OpenAiCompatibleNamePattern.IsMatch(value))
        {
            throw new InvalidOperationException($"{fieldName} must contain only letters, numbers, underscores, or dashes.");
        }

        return value;
    }

    private static int TryGetUsageInt(JsonElement root, params string[] propertyNames)
    {
        if (root.TryGetProperty("usage", out var usage)
            && usage.ValueKind == JsonValueKind.Object)
        {
            var usageValue = TryGetPositiveInt(usage, propertyNames);
            if (usageValue > 0)
            {
                return usageValue;
            }
        }

        return TryGetPositiveInt(root, propertyNames);
    }

    private static int TryGetPositiveInt(JsonElement parent, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!parent.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            var parsed = value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
                _ => 0
            };
            if (parsed > 0)
            {
                return parsed;
            }
        }

        return 0;
    }

    private static string? TryGetResponseId(JsonElement root)
    {
        return TryGetTrimmedString(root, "id", "response_id", "request_id", "responseId", "requestId");
    }

    private static string? TryGetResponseServiceTier(JsonElement root)
    {
        var serviceTier = TryGetTrimmedString(root, "service_tier", "serviceTier");
        return string.IsNullOrWhiteSpace(serviceTier)
            ? null
            : serviceTier.ToLowerInvariant();
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
        if (!TryGetProperty(firstChoice, out var finishReason, "finish_reason", "finishReason"))
        {
            return InferFinishReason(firstChoice);
        }

        return finishReason.ValueKind switch
        {
            JsonValueKind.String => NormalizeFinishReason(finishReason.GetString()),
            _ => InferFinishReason(firstChoice)
        };
    }

    private static string? InferFinishReason(JsonElement firstChoice)
    {
        if (TryHasNativeToolCalls(firstChoice))
        {
            return GenerateTextFinishReasons.ToolCall;
        }

        if (!firstChoice.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryHasNativeToolCalls(message)
            ? GenerateTextFinishReasons.ToolCall
            : null;
    }

    private static string? NormalizeFinishReason(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason))
        {
            return null;
        }

        return finishReason.Trim().ToLowerInvariant() switch
        {
            "stop" => GenerateTextFinishReasons.Completed,
            "tool_calls" => GenerateTextFinishReasons.ToolCall,
            "tool_call" => GenerateTextFinishReasons.ToolCall,
            "toolcall" => GenerateTextFinishReasons.ToolCall,
            "toolcalls" => GenerateTextFinishReasons.ToolCall,
            "function_call" => GenerateTextFinishReasons.ToolCall,
            "functioncall" => GenerateTextFinishReasons.ToolCall,
            "length" => GenerateTextFinishReasons.MaxOutputTokens,
            "max_output_tokens" => GenerateTextFinishReasons.MaxOutputTokens,
            "maxoutputtokens" => GenerateTextFinishReasons.MaxOutputTokens,
            "content_filter" => GenerateTextFinishReasons.ContentFiltered,
            "content_filtered" => GenerateTextFinishReasons.ContentFiltered,
            "contentfilter" => GenerateTextFinishReasons.ContentFiltered,
            "contentfiltered" => GenerateTextFinishReasons.ContentFiltered,
            var value => value
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
        if (firstChoice.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object)
        {
            if (TryCollectText(message, out var reasoningSummary, "reasoning_summary", "reasoningSummary"))
            {
                return reasoningSummary;
            }

            if (TryCollectText(message, out var reasoning, "reasoning"))
            {
                return reasoning;
            }
        }

        if (TryCollectText(firstChoice, out var choiceReasoningSummary, "reasoning_summary", "reasoningSummary"))
        {
            return choiceReasoningSummary;
        }

        return TryCollectText(firstChoice, out var choiceReasoning, "reasoning")
            ? choiceReasoning
            : null;
    }

    private static IReadOnlyList<GenerateTextToolCall>? TryGetToolCalls(
        JsonElement root,
        IReadOnlyList<GenerateTextToolDefinition>? declaredTools)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];
        if (TryGetToolCallsFromContainer(firstChoice, declaredTools, out var choiceLevelToolCalls))
        {
            return choiceLevelToolCalls;
        }

        if (!firstChoice.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetToolCallsFromContainer(message, declaredTools, out var messageLevelToolCalls)
            ? messageLevelToolCalls
            : null;
    }

    private static bool TryGetToolCallsFromContainer(
        JsonElement container,
        IReadOnlyList<GenerateTextToolDefinition>? declaredTools,
        out IReadOnlyList<GenerateTextToolCall>? toolCalls)
    {
        if (TryGetProperty(container, out var nativeToolCalls, "tool_calls", "toolCalls"))
        {
            if (nativeToolCalls.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Model gateway returned a native tool_calls payload that is not an array.");
            }

            var calls = new List<GenerateTextToolCall>();
            foreach (var toolCall in nativeToolCalls.EnumerateArray())
            {
                calls.Add(ParseNativeToolCall(toolCall, declaredTools));
            }

            if (calls.Count > 0)
            {
                toolCalls = calls;
                return true;
            }
        }

        if (TryGetProperty(container, out var functionCall, "function_call", "functionCall"))
        {
            if (functionCall.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Model gateway returned a legacy function_call payload that is not an object.");
            }

            toolCalls =
            [
                ParseLegacyFunctionCall(functionCall, declaredTools)
            ];
            return true;
        }

        toolCalls = null;
        return false;
    }

    private static bool TryHasNativeToolCalls(JsonElement container)
    {
        if (TryGetProperty(container, out var toolCalls, "tool_calls", "toolCalls")
            && toolCalls.ValueKind == JsonValueKind.Array
            && toolCalls.GetArrayLength() > 0)
        {
            return true;
        }

        return TryGetProperty(container, out var functionCall, "function_call", "functionCall")
            && functionCall.ValueKind == JsonValueKind.Object;
    }

    private static GenerateTextToolCall ParseNativeToolCall(
        JsonElement toolCall,
        IReadOnlyList<GenerateTextToolDefinition>? declaredTools)
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

        return ParseFunctionToolCall(function, id.Trim(), type!.Trim(), declaredTools);
    }

    private static GenerateTextToolCall ParseLegacyFunctionCall(
        JsonElement functionCall,
        IReadOnlyList<GenerateTextToolDefinition>? declaredTools)
    {
        return ParseFunctionToolCall(functionCall, "legacy_function_call", "function", declaredTools);
    }

    private static GenerateTextToolCall ParseFunctionToolCall(
        JsonElement function,
        string callId,
        string callType,
        IReadOnlyList<GenerateTextToolDefinition>? declaredTools)
    {
        var toolName = function.TryGetProperty("name", out var nameElement)
            && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new InvalidOperationException("Model gateway returned a native tool call without a function name.");
        }

        var toolArguments = TryGetNativeToolCallArguments(toolName, function);
        ValidateNativeToolCall(toolName, toolArguments, declaredTools);

        return new GenerateTextToolCall(
            callId,
            callType,
            toolName.Trim(),
            string.IsNullOrWhiteSpace(toolArguments) ? null : toolArguments.Trim());
    }

    private static string? TryGetNativeToolCallArguments(string toolName, JsonElement function)
    {
        if (!function.TryGetProperty("arguments", out var argumentsElement))
        {
            return null;
        }

        return argumentsElement.ValueKind switch
        {
            JsonValueKind.String => argumentsElement.GetString(),
            JsonValueKind.Object => JsonSerializer.Serialize(argumentsElement, JsonOptions),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => throw new InvalidOperationException(
                $"Model gateway returned native tool call arguments for '{toolName.Trim()}' that are not a JSON object.")
        };
    }

    private static void ValidateNativeToolCall(
        string toolName,
        string? toolArguments,
        IReadOnlyList<GenerateTextToolDefinition>? declaredTools)
    {
        if (declaredTools is null || declaredTools.Count == 0)
        {
            throw new InvalidOperationException(
                $"Model gateway returned undeclared native tool call '{toolName.Trim()}', but no tools were declared for this request.");
        }

        var matchedTool = declaredTools.FirstOrDefault(tool =>
            string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (matchedTool is null)
        {
            throw new InvalidOperationException(
                $"Model gateway returned undeclared native tool call '{toolName.Trim()}'.");
        }

        using var parsedArguments = ParseNativeToolCallArguments(toolName, toolArguments);
        ValidateNativeToolCallArgumentsAgainstSchema(matchedTool, parsedArguments.RootElement);
    }

    private static JsonDocument ParseNativeToolCallArguments(string toolName, string? toolArguments)
    {
        if (string.IsNullOrWhiteSpace(toolArguments))
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            var document = JsonDocument.Parse(toolArguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new InvalidOperationException(
                    $"Model gateway returned native tool call arguments for '{toolName.Trim()}' that are not a JSON object.");
            }

            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Model gateway returned invalid native tool call arguments for '{toolName.Trim()}'.",
                ex);
        }
    }

    private static void ValidateNativeToolCallArgumentsAgainstSchema(
        GenerateTextToolDefinition declaredTool,
        JsonElement arguments)
    {
        if (string.IsNullOrWhiteSpace(declaredTool.InputSchema))
        {
            return;
        }

        var toolName = declaredTool.Name.Trim();
        var schema = JsonSchemaToolValidation.ParseSchema(toolName, declaredTool.InputSchema, "Input");
        if (!JsonSchemaToolValidation.TryValidateElement(
                toolName,
                "$",
                arguments,
                schema,
                out var error,
                "Input"))
        {
            throw new InvalidOperationException(error);
        }
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
        if (TryBuildNativeToolCallDecision(toolCalls, out var toolCallDecision))
        {
            return toolCallDecision;
        }

        if (firstChoice.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object)
        {
            if (TryCollectText(message, out var messageText, "content", "output_text", "outputText", "text"))
            {
                return messageText;
            }

            if (TryCollectText(message, out var refusalText, "refusal"))
            {
                return refusalText;
            }
        }

        if (TryCollectText(firstChoice, out var choiceText, "text", "output_text", "outputText"))
        {
            return choiceText;
        }

        return TryCollectText(firstChoice, out var choiceRefusalText, "refusal")
            ? choiceRefusalText
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

    private static bool TryCollectText(JsonElement parent, out string? text, params string[] propertyNames)
    {
        text = null;

        foreach (var propertyName in propertyNames)
        {
            if (!parent.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            var segments = new List<string>();
            CollectReasoningSegments(value, segments);
            if (segments.Count == 0)
            {
                continue;
            }

            text = string.Join("\n", segments);
            return true;
        }

        return false;
    }

    private static bool TryGetProperty(JsonElement parent, out JsonElement value, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (parent.TryGetProperty(propertyName, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (TryCollectErrorText(root, out var errorMessage, "message", "detail", "title", "description", "error_description", "errorDescription"))
            {
                return errorMessage;
            }

            if (TryGetProperty(root, out var errorElement, "error"))
            {
                if (errorElement.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(errorElement.GetString()))
                {
                    return errorElement.GetString()!.Trim();
                }

                if (TryCollectErrorTextFromElement(errorElement, out var nestedErrorMessage))
                {
                    return nestedErrorMessage;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryCollectErrorTextFromElement(JsonElement value, out string? text)
    {
        var segments = new List<string>();
        CollectErrorSegments(value, segments);
        if (segments.Count == 0)
        {
            text = null;
            return false;
        }

        text = string.Join("\n", segments);
        return true;
    }

    private static bool TryCollectErrorText(JsonElement parent, out string? text, params string[] propertyNames)
    {
        text = null;

        foreach (var propertyName in propertyNames)
        {
            if (!parent.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (TryCollectErrorTextFromElement(value, out text))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetTrimmedString(JsonElement parent, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!parent.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                continue;
            }

            return value.GetString()!.Trim();
        }

        return null;
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

                if (value.TryGetProperty("output_text", out var outputTextProperty))
                {
                    CollectReasoningSegments(outputTextProperty, segments);
                }

                if (value.TryGetProperty("outputText", out var outputTextCamelCaseProperty))
                {
                    CollectReasoningSegments(outputTextCamelCaseProperty, segments);
                }

                if (value.TryGetProperty("value", out var valueProperty))
                {
                    CollectReasoningSegments(valueProperty, segments);
                }

                if (value.TryGetProperty("summary", out var summaryProperty))
                {
                    CollectReasoningSegments(summaryProperty, segments);
                }

                if (value.TryGetProperty("reasoning_summary", out var reasoningSummaryProperty))
                {
                    CollectReasoningSegments(reasoningSummaryProperty, segments);
                }

                if (value.TryGetProperty("reasoningSummary", out var reasoningSummaryCamelCaseProperty))
                {
                    CollectReasoningSegments(reasoningSummaryCamelCaseProperty, segments);
                }

                if (value.TryGetProperty("content", out var contentProperty))
                {
                    CollectReasoningSegments(contentProperty, segments);
                }

                break;
        }
    }

    private static void CollectErrorSegments(JsonElement value, List<string> segments)
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
                    CollectErrorSegments(item, segments);
                }

                break;
            case JsonValueKind.Object:
                if (value.TryGetProperty("message", out var messageProperty))
                {
                    CollectErrorSegments(messageProperty, segments);
                }

                if (value.TryGetProperty("detail", out var detailProperty))
                {
                    CollectErrorSegments(detailProperty, segments);
                }

                if (value.TryGetProperty("error", out var errorProperty))
                {
                    CollectErrorSegments(errorProperty, segments);
                }

                if (value.TryGetProperty("title", out var titleProperty))
                {
                    CollectErrorSegments(titleProperty, segments);
                }

                if (value.TryGetProperty("msg", out var msgProperty))
                {
                    CollectErrorSegments(msgProperty, segments);
                }

                if (value.TryGetProperty("description", out var descriptionProperty))
                {
                    CollectErrorSegments(descriptionProperty, segments);
                }

                if (value.TryGetProperty("error_description", out var errorDescriptionProperty))
                {
                    CollectErrorSegments(errorDescriptionProperty, segments);
                }

                if (value.TryGetProperty("errorDescription", out var errorDescriptionCamelCaseProperty))
                {
                    CollectErrorSegments(errorDescriptionCamelCaseProperty, segments);
                }

                if (value.TryGetProperty("content", out var contentProperty))
                {
                    CollectErrorSegments(contentProperty, segments);
                }

                if (value.TryGetProperty("text", out var textProperty))
                {
                    CollectErrorSegments(textProperty, segments);
                }

                if (value.TryGetProperty("value", out var valueProperty))
                {
                    CollectErrorSegments(valueProperty, segments);
                }

                break;
        }
    }
}
