using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BestAgent.Application.Abstractions;
using BestAgent.Application.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BestAgent.Infrastructure.Model;

internal sealed class OpenAiCompatibleModelGateway : IModelGateway
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiCompatibleModelGateway> _logger;

    public OpenAiCompatibleModelGateway(
        HttpClient httpClient,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiCompatibleModelGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PlanDecision> PlanAsync(ModelContext context, CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(context);
        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(context.ModelName) ? _options.Model : context.ModelName,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = prompt
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(payload);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Model returned an empty content payload.");
        }

        _logger.LogInformation("Model response received for agent {AgentCode}", context.AgentCode);
        return PlanDecision.Parse(content);
    }

    private static object[] BuildPrompt(ModelContext context)
    {
        var messages = new List<object>
        {
            new
            {
                role = "system",
                content =
                    """
                    You are the planner for an MVP agent runtime.
                    You must return strict JSON with fields:
                    type, reason, responseMessage, selectedModel, toolCalls.
                    Allowed type values: respond, tool_call.
                    If type is respond, include responseMessage.
                    If type is tool_call, include exactly one item in toolCalls with toolName and arguments.
                    Never include markdown fences.
                    """
            },
            new
            {
                role = "system",
                content = $"Agent instruction: {context.Instruction}"
            }
        };

        messages.AddRange(context.Messages.Select(message => new { role = message.Role, content = message.Content }));
        return messages.ToArray();
    }
}
