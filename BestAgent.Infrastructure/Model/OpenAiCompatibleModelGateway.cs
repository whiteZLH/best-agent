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
            var temperature = NormalizeTemperature(request.Temperature ?? _options.Temperature);
            var maxOutputTokens = NormalizeMaxOutputTokens(request.MaxOutputTokens ?? _options.MaxOutputTokens);

            var payload = new
            {
                model,
                messages = BuildMessages(request),
                temperature,
                max_tokens = maxOutputTokens
            };
            _logger.LogDebug(
                "Calling model {Model} with temperature {Temperature}, max tokens {MaxOutputTokens}, system prompt length {SystemPromptLength} and input length {InputLength}",
                model,
                temperature,
                maxOutputTokens,
                request.SystemPrompt?.Length ?? 0,
                request.Input?.Length ?? 0);

            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
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
                CalculateCost(promptTokens, completionTokens));

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
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogInformation(
                "Model call completed for {Model} in {DurationMs}ms with {TotalTokens} total tokens and cost {Cost}",
                model,
                (DateTime.UtcNow - startedAt).TotalMilliseconds,
                result.TotalTokens,
                result.Cost);

            return result;
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
}
