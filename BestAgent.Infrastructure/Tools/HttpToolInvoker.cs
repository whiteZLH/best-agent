using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BestAgent.Application.Tools;

namespace BestAgent.Infrastructure.Tools;

public class HttpToolInvoker : IHttpToolInvoker
{
    private const string ClientName = "ToolWebhook";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpToolInvoker(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ToolExecutionResult> InvokeAsync(
        HttpToolInvocationRequest request,
        CancellationToken cancellationToken)
    {
        var retryPolicy = ToolRetryPolicy.Parse(request.RetryPolicy);
        ToolExecutionResult? lastFailureResult = null;

        for (var attempt = 1; attempt <= retryPolicy.MaxAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(request.TimeoutMs));

            try
            {
                using var httpRequest = BuildRequest(request);
                var client = _httpClientFactory.CreateClient(ClientName);
                using var response = await client.SendAsync(httpRequest, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    lastFailureResult = ToolExecutionResult.Failed(
                        request.ToolName,
                        $"Tool webhook failed with HTTP {(int)response.StatusCode}: {body}");
                    if (attempt < retryPolicy.MaxAttempts && IsRetryableStatusCode(response.StatusCode))
                    {
                        await DelayBeforeRetryAsync(retryPolicy, cancellationToken);
                        continue;
                    }

                    return lastFailureResult;
                }

                return ParseResult(request.ToolName, body);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailureResult = ToolExecutionResult.Failed(request.ToolName, $"Tool webhook timed out after {request.TimeoutMs}ms.");
                if (attempt < retryPolicy.MaxAttempts)
                {
                    await DelayBeforeRetryAsync(retryPolicy, cancellationToken);
                    continue;
                }

                return lastFailureResult;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailureResult = ToolExecutionResult.Failed(request.ToolName, $"Tool webhook failed: {ex.Message}");
                if (attempt < retryPolicy.MaxAttempts)
                {
                    await DelayBeforeRetryAsync(retryPolicy, cancellationToken);
                    continue;
                }

                return lastFailureResult;
            }
        }

        return lastFailureResult ?? ToolExecutionResult.Failed(request.ToolName, "Tool webhook failed.");
    }

    private static HttpRequestMessage BuildRequest(HttpToolInvocationRequest request)
    {
        var httpRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), request.EndpointUrl);
        AddAuthHeaders(httpRequest, request.AuthHeaders);
        AddIdempotencyHeader(httpRequest, request.IdempotencyKey);

        var payload = new
        {
            toolName = request.ToolName,
            input = request.Input,
            inputJson = ParseJsonOrNull(request.Input),
            inputSchema = ParseJsonOrNull(request.InputSchema),
            outputSchema = ParseJsonOrNull(request.OutputSchema),
            context = new
            {
                runId = request.Context.RunId,
                agentCode = request.Context.AgentCode,
                userInput = request.Context.UserInput
            }
        };

        httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);
        return httpRequest;
    }

    private static void AddIdempotencyHeader(HttpRequestMessage request, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.Trim());
    }

    private static void AddAuthHeaders(HttpRequestMessage request, string? authHeaders)
    {
        if (string.IsNullOrWhiteSpace(authHeaders))
        {
            return;
        }

        using var document = JsonDocument.Parse(authHeaders);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(property.Name, property.Value.GetString());
        }
    }

    private static JsonElement? ParseJsonOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ToolExecutionResult ParseResult(string toolName, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
                return ToolExecutionResult.Completed(toolName, string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && TryParseStandardEnvelope(toolName, root, out var standardResult))
            {
                return standardResult;
            }

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("output", out var outputElement))
            {
                return ToolExecutionResult.Completed(toolName, body);
            }

            var output = outputElement.ValueKind == JsonValueKind.String
                ? outputElement.GetString() ?? string.Empty
                : outputElement.GetRawText();
            var isPending = root.TryGetProperty("isPending", out var pendingElement)
                && pendingElement.ValueKind == JsonValueKind.True;
            var waitToken = root.TryGetProperty("waitToken", out var waitTokenElement)
                && waitTokenElement.ValueKind == JsonValueKind.String
                    ? waitTokenElement.GetString()
                    : null;
            var meta = root.TryGetProperty("meta", out var legacyMetaElement)
                ? legacyMetaElement.GetRawText()
                : null;

            return isPending
                ? ToolExecutionResult.Pending(toolName, waitToken ?? Guid.NewGuid().ToString("N"), meta)
                : ToolExecutionResult.Completed(toolName, output, meta);
        }
        catch (JsonException)
        {
            return ToolExecutionResult.Completed(toolName, body);
        }
    }

    private static bool TryParseStandardEnvelope(
        string toolName,
        JsonElement root,
        out ToolExecutionResult result)
    {
        result = default!;

        if (!root.TryGetProperty("status", out var statusElement)
            || statusElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var status = statusElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        if (IsPendingStatus(status))
        {
            var waitToken = ReadWaitToken(root);
            result = ToolExecutionResult.Pending(toolName, waitToken ?? Guid.NewGuid().ToString("N"), ReadMeta(root));
            return true;
        }

        if (IsSucceededStatus(status))
        {
            var output = root.TryGetProperty("data", out var dataElement)
                ? SerializeResultPayload(dataElement)
                : root.TryGetProperty("output", out var outputElement)
                    ? SerializeResultPayload(outputElement)
                    : string.Empty;
            result = ToolExecutionResult.Completed(toolName, output, ReadMeta(root));
            return true;
        }

        if (IsFailureStatus(status))
        {
            var error = root.TryGetProperty("error", out var errorElement)
                ? SerializeResultPayload(errorElement)
                : root.GetRawText();
            result = ToolExecutionResult.Failed(toolName, error, ReadMeta(root));
            return true;
        }

        return false;
    }

    private static bool IsPendingStatus(string status)
    {
        return status.Equals("pending", StringComparison.OrdinalIgnoreCase)
            || status.Equals("running", StringComparison.OrdinalIgnoreCase)
            || status.Equals("waiting", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSucceededStatus(string status)
    {
        return status.Equals("succeeded", StringComparison.OrdinalIgnoreCase)
            || status.Equals("success", StringComparison.OrdinalIgnoreCase)
            || status.Equals("completed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailureStatus(string status)
    {
        return status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("error", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadWaitToken(JsonElement root)
    {
        if (root.TryGetProperty("waitToken", out var waitTokenElement)
            && waitTokenElement.ValueKind == JsonValueKind.String)
        {
            return waitTokenElement.GetString();
        }

        if (root.TryGetProperty("meta", out var metaElement)
            && metaElement.ValueKind == JsonValueKind.Object
            && metaElement.TryGetProperty("waitToken", out var metaWaitToken)
            && metaWaitToken.ValueKind == JsonValueKind.String)
        {
            return metaWaitToken.GetString();
        }

        return null;
    }

    private static string SerializeResultPayload(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText();
    }

    private static string? ReadMeta(JsonElement root)
    {
        return root.TryGetProperty("meta", out var metaElement)
            ? metaElement.GetRawText()
            : null;
    }

    private static bool IsRetryableStatusCode(System.Net.HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status == 408 || status == 429 || status >= 500;
    }

    private static Task DelayBeforeRetryAsync(ToolRetryPolicy retryPolicy, CancellationToken cancellationToken)
    {
        return retryPolicy.DelayMs <= 0
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromMilliseconds(retryPolicy.DelayMs), cancellationToken);
    }

    private sealed record ToolRetryPolicy(int MaxAttempts, int DelayMs)
    {
        public static ToolRetryPolicy Parse(string? retryPolicy)
        {
            if (string.IsNullOrWhiteSpace(retryPolicy))
            {
                return new ToolRetryPolicy(1, 0);
            }

            var trimmed = retryPolicy.Trim();
            if (string.Equals(trimmed, "retry-once", StringComparison.OrdinalIgnoreCase))
            {
                return new ToolRetryPolicy(2, 0);
            }

            if (string.Equals(trimmed, "retry-twice", StringComparison.OrdinalIgnoreCase))
            {
                return new ToolRetryPolicy(3, 0);
            }

            try
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new ToolRetryPolicy(1, 0);
                }

                var maxAttempts = ReadPositiveInt(document.RootElement, "maxAttempts", 1);
                var delayMs = ReadPositiveInt(document.RootElement, "delayMs", 0);
                return new ToolRetryPolicy(Math.Clamp(maxAttempts, 1, 5), Math.Clamp(delayMs, 0, 10_000));
            }
            catch (JsonException)
            {
                return new ToolRetryPolicy(1, 0);
            }
        }

        private static int ReadPositiveInt(JsonElement root, string propertyName, int defaultValue)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Number
                || !property.TryGetInt32(out var value))
            {
                return defaultValue;
            }

            return Math.Max(0, value);
        }
    }
}
