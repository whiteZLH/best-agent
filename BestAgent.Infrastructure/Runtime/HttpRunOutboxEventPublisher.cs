using System.Text;
using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;

namespace BestAgent.Infrastructure.Runtime;

public sealed class HttpRunOutboxEventPublisher : IRunOutboxEventPublisher
{
    private const string ClientName = "RunOutboxPublisher";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RunOutboxPublisherOptions _options;

    public HttpRunOutboxEventPublisher(
        IHttpClientFactory httpClientFactory,
        RunOutboxPublisherOptions? options = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options ?? new RunOutboxPublisherOptions();
    }

    public async Task PublishAsync(RunOutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_options.EndpointUrl))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.EndpointUrl.Trim());
        if (!string.IsNullOrWhiteSpace(_options.AuthorizationHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", _options.AuthorizationHeader.Trim());
        }

        var payload = new
        {
            outboxEvent.EventId,
            outboxEvent.RunId,
            outboxEvent.SeqNo,
            outboxEvent.EventType,
            outboxEvent.RunStatus,
            outboxEvent.Payload,
            outboxEvent.RetryCount,
            outboxEvent.OccurredAt
        };
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var client = _httpClientFactory.CreateClient(ClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
