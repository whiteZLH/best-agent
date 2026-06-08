using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentRuns;

namespace BestAgent.Infrastructure.Runtime;

public sealed class HttpRunOutboxEventPublisher : IRunOutboxEventPublisher
{
    private const string ClientName = "RunOutboxPublisher";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RunOutboxPublisherOptions _options;
    private readonly IAgentMetrics _agentMetrics;

    public HttpRunOutboxEventPublisher(
        IHttpClientFactory httpClientFactory,
        RunOutboxPublisherOptions? options = null,
        IAgentMetrics? agentMetrics = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options ?? new RunOutboxPublisherOptions();
        _agentMetrics = agentMetrics ?? NullAgentMetrics.Instance;
    }

    public async Task PublishAsync(RunOutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTime.UtcNow;
        using var activity = AgentTracing.Source.StartActivity(AgentTracing.OutboxPublishActivityName, ActivityKind.Client);
        activity?.SetTag("bestagent.event_id", outboxEvent.EventId);
        activity?.SetTag("bestagent.run_id", outboxEvent.RunId);
        activity?.SetTag("bestagent.seq_no", outboxEvent.SeqNo);
        activity?.SetTag("bestagent.event_type", outboxEvent.EventType);
        activity?.SetTag("bestagent.retry_count", outboxEvent.RetryCount);
        activity?.SetTag("bestagent.publish_status", outboxEvent.PublishStatus);
        activity?.SetTag("bestagent.outbox_endpoint_configured", !string.IsNullOrWhiteSpace(_options.EndpointUrl));
        var isRetry = outboxEvent.RetryCount > 0;

        if (string.IsNullOrWhiteSpace(_options.EndpointUrl))
        {
            activity?.SetTag("bestagent.status", "skipped");
            _agentMetrics.RecordOutboxPublish(
                outboxEvent.EventType,
                "skipped",
                isRetry,
                DateTime.UtcNow - startedAt);
            return;
        }

        try
        {
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
            activity?.SetTag("bestagent.status", "completed");
            activity?.SetTag("bestagent.http_status_code", (int)response.StatusCode);
            _agentMetrics.RecordOutboxPublish(
                outboxEvent.EventType,
                "completed",
                isRetry,
                DateTime.UtcNow - startedAt);
        }
        catch
        {
            activity?.SetTag("bestagent.status", "failed");
            _agentMetrics.RecordOutboxPublish(
                outboxEvent.EventType,
                "failed",
                isRetry,
                DateTime.UtcNow - startedAt);
            throw;
        }
    }
}
