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
                return ToolExecutionResult.Completed(
                    request.ToolName,
                    $"Tool webhook failed with HTTP {(int)response.StatusCode}: {body}");
            }

            return ParseResult(request.ToolName, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolExecutionResult.Completed(request.ToolName, $"Tool webhook timed out after {request.TimeoutMs}ms.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExecutionResult.Completed(request.ToolName, $"Tool webhook failed: {ex.Message}");
        }
    }

    private static HttpRequestMessage BuildRequest(HttpToolInvocationRequest request)
    {
        var httpRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), request.EndpointUrl);
        AddAuthHeaders(httpRequest, request.AuthHeaders);

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

            return isPending
                ? ToolExecutionResult.Pending(toolName, waitToken ?? Guid.NewGuid().ToString("N"))
                : ToolExecutionResult.Completed(toolName, output);
        }
        catch (JsonException)
        {
            return ToolExecutionResult.Completed(toolName, body);
        }
    }
}
