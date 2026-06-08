using System.Net;
using System.Net.Http;
using System.Text.Json;
using BestAgent.Api.Tests.Observability;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Runtime;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class HttpRunOutboxEventPublisherTests
{
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
    private readonly IAgentMetrics _agentMetrics = Substitute.For<IAgentMetrics>();

    [Fact]
    public async Task PublishAsync_ShouldRecordSkippedAttempt_WhenEndpointIsNotConfigured()
    {
        using var activities = new ActivityTestCollector(AgentTracing.SourceName);
        var publisher = new HttpRunOutboxEventPublisher(
            _httpClientFactory,
            new RunOutboxPublisherOptions(),
            _agentMetrics);

        await publisher.PublishAsync(CreateOutboxEvent(), CancellationToken.None);

        _httpClientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
        _agentMetrics.Received(1).RecordOutboxPublish(
            "done",
            "skipped",
            true,
            Arg.Any<TimeSpan>());
        var activity = Assert.Single(
            activities.Activities,
            value => value.OperationName == AgentTracing.OutboxPublishActivityName);
        Assert.Equal(AgentTracing.OutboxPublishActivityName, activity.OperationName);
        Assert.Equal("event-1", activity.GetTagItem("bestagent.event_id"));
        Assert.Equal("skipped", activity.GetTagItem("bestagent.status"));
        Assert.Equal(false, activity.GetTagItem("bestagent.outbox_endpoint_configured"));
    }

    [Fact]
    public async Task PublishAsync_ShouldPostExpectedPayloadAndRecordTraceMetrics()
    {
        using var activities = new ActivityTestCollector(AgentTracing.SourceName);
        HttpMethod? capturedMethod = null;
        string? capturedUri = null;
        string[]? capturedAuthorizationValues = null;
        JsonElement? capturedPayload = null;
        using var client = CreateClient(async request =>
        {
            capturedMethod = request.Method;
            capturedUri = request.RequestUri?.ToString();
            if (request.Headers.TryGetValues("Authorization", out var authValues))
            {
                capturedAuthorizationValues = authValues.ToArray();
            }

            capturedPayload = await ReadJsonAsync(request.Content!);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        _httpClientFactory.CreateClient("RunOutboxPublisher").Returns(client);
        var publisher = new HttpRunOutboxEventPublisher(
            _httpClientFactory,
            new RunOutboxPublisherOptions
            {
                EndpointUrl = "https://example.com/outbox/events",
                AuthorizationHeader = "Bearer outbox-token"
            },
            _agentMetrics);

        await publisher.PublishAsync(CreateOutboxEvent(), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("https://example.com/outbox/events", capturedUri);
        Assert.NotNull(capturedAuthorizationValues);
        Assert.Equal("Bearer outbox-token", Assert.Single(capturedAuthorizationValues!));
        Assert.True(capturedPayload.HasValue);
        var payload = capturedPayload.Value;
        Assert.Equal("event-1", payload.GetProperty("eventId").GetString());
        Assert.Equal("run-1", payload.GetProperty("runId").GetString());
        Assert.Equal(1, payload.GetProperty("seqNo").GetInt64());
        Assert.Equal("done", payload.GetProperty("eventType").GetString());
        Assert.Equal("Completed", payload.GetProperty("runStatus").GetString());
        Assert.Equal("{\"stepNo\":0}", payload.GetProperty("payload").GetString());
        Assert.Equal(2, payload.GetProperty("retryCount").GetInt32());
        _agentMetrics.Received(1).RecordOutboxPublish(
            "done",
            "completed",
            true,
            Arg.Any<TimeSpan>());
        var activity = Assert.Single(
            activities.Activities,
            value => value.OperationName == AgentTracing.OutboxPublishActivityName);
        Assert.Equal(AgentTracing.OutboxPublishActivityName, activity.OperationName);
        Assert.Equal("event-1", activity.GetTagItem("bestagent.event_id"));
        Assert.Equal("run-1", activity.GetTagItem("bestagent.run_id"));
        Assert.Equal(1L, activity.GetTagItem("bestagent.seq_no"));
        Assert.Equal("done", activity.GetTagItem("bestagent.event_type"));
        Assert.Equal(2, activity.GetTagItem("bestagent.retry_count"));
        Assert.Equal("completed", activity.GetTagItem("bestagent.status"));
        Assert.Equal((int)HttpStatusCode.Accepted, activity.GetTagItem("bestagent.http_status_code"));
    }

    [Fact]
    public async Task PublishAsync_ShouldRecordFailedAttempt_WhenHttpCallFails()
    {
        using var activities = new ActivityTestCollector(AgentTracing.SourceName);
        using var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        _httpClientFactory.CreateClient("RunOutboxPublisher").Returns(client);
        var publisher = new HttpRunOutboxEventPublisher(
            _httpClientFactory,
            new RunOutboxPublisherOptions
            {
                EndpointUrl = "https://example.com/outbox/events"
            },
            _agentMetrics);

        await Assert.ThrowsAsync<HttpRequestException>(() => publisher.PublishAsync(CreateOutboxEvent(), CancellationToken.None));

        _agentMetrics.Received(1).RecordOutboxPublish(
            "done",
            "failed",
            true,
            Arg.Any<TimeSpan>());
        var activity = Assert.Single(
            activities.Activities,
            value => value.OperationName == AgentTracing.OutboxPublishActivityName);
        Assert.Equal(AgentTracing.OutboxPublishActivityName, activity.OperationName);
        Assert.Equal("failed", activity.GetTagItem("bestagent.status"));
    }

    private static RunOutboxEvent CreateOutboxEvent()
    {
        var now = new DateTime(2026, 6, 9, 0, 0, 0, DateTimeKind.Utc);
        return new RunOutboxEvent
        {
            EventId = "event-1",
            RunId = "run-1",
            SeqNo = 1,
            EventType = "done",
            RunStatus = "Completed",
            Payload = "{\"stepNo\":0}",
            RetryCount = 2,
            PublishStatus = "pending",
            OccurredAt = now,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        return CreateClient((request, _) => handler(request));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new StubHttpMessageHandler(handler), disposeHandler: true);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpContent content)
    {
        using var document = JsonDocument.Parse(await content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
