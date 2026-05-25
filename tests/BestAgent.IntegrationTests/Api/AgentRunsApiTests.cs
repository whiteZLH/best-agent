using System.Net;
using System.Net.Http.Json;
using BestAgent.Application.Planning;
using BestAgent.Contracts.AgentRuns;
using BestAgent.Domain.Agents;
using BestAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BestAgent.IntegrationTests.Api;

public sealed class AgentRunsApiTests
{
    [Fact]
    public async Task PostAgentRuns_ShouldCompleteRespondFlow()
    {
        await using var host = await TestApiHost.CreateAsync([
            () => new PlanDecision(PlanDecisionType.Respond, "enough", "done", [], "test-model")
        ]);
        var client = host.Client;

        var response = await client.PostAsJsonAsync("/agent-runs", CreateRequest("idem-respond", "hello"));

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AgentRunResponse>();

        Assert.NotNull(payload);
        Assert.Equal("Completed", payload.Status);
        Assert.Contains("done", payload.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAgentRuns_ShouldCompleteToolCallThenRespondFlow()
    {
        await using var host = await TestApiHost.CreateAsync([
            () => new PlanDecision(PlanDecisionType.ToolCall, "need tool", null, [new ToolCallPlan("echo_context", """{"text":"hello"}""")], "test-model"),
            () => new PlanDecision(PlanDecisionType.Respond, "done", "tool complete", [], "test-model")
        ]);
        var client = host.Client;

        var response = await client.PostAsJsonAsync("/agent-runs", CreateRequest("idem-tool", "echo this"));

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AgentRunResponse>();

        Assert.NotNull(payload);
        Assert.Equal("Completed", payload.Status);
        Assert.Contains("tool complete", payload.Output, StringComparison.OrdinalIgnoreCase);

        var stepsResponse = await client.GetFromJsonAsync<List<AgentStepResponse>>($"/agent-runs/{payload.RunId}/steps");
        Assert.NotNull(stepsResponse);
        Assert.Contains(stepsResponse, step => step.StepType == "ToolCall");
        Assert.Contains(stepsResponse, step => step.StepType == "ToolResult");
    }

    [Fact]
    public async Task PostAgentRuns_ShouldBeIdempotent()
    {
        await using var host = await TestApiHost.CreateAsync([
            () => new PlanDecision(PlanDecisionType.Respond, "enough", "done", [], "test-model")
        ]);
        var client = host.Client;
        var request = CreateRequest("idem-repeat", "hello");

        var first = await client.PostAsJsonAsync("/agent-runs", request);
        var second = await client.PostAsJsonAsync("/agent-runs", request);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var firstPayload = await first.Content.ReadFromJsonAsync<AgentRunResponse>();
        var secondPayload = await second.Content.ReadFromJsonAsync<AgentRunResponse>();

        Assert.Equal(firstPayload?.RunId, secondPayload?.RunId);
    }

    [Fact]
    public async Task GetAgentRun_ShouldReturnCompletedRun()
    {
        await using var host = await TestApiHost.CreateAsync([
            () => new PlanDecision(PlanDecisionType.Respond, "enough", "done", [], "test-model")
        ]);
        var client = host.Client;

        var createResponse = await client.PostAsJsonAsync("/agent-runs", CreateRequest("idem-get", "hello"));
        var created = await createResponse.Content.ReadFromJsonAsync<AgentRunResponse>();

        var response = await client.GetAsync($"/agent-runs/{created!.RunId}");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AgentRunResponse>();
        Assert.Equal(created.RunId, payload?.RunId);
        Assert.Equal("Completed", payload?.Status);
    }

    [Fact]
    public async Task PostAgentRuns_ShouldFail_WhenModelGatewayThrows()
    {
        await using var host = await TestApiHost.CreateAsync([
            () => throw new InvalidOperationException("Invalid plan JSON")
        ]);
        var client = host.Client;

        var response = await client.PostAsJsonAsync("/agent-runs", CreateRequest("idem-fail-model", "hello"));
        var payload = await response.Content.ReadFromJsonAsync<AgentRunResponse>();

        response.EnsureSuccessStatusCode();
        Assert.Equal("Failed", payload?.Status);
        Assert.Contains("Invalid plan JSON", payload?.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAgentRuns_ShouldFail_WhenToolIsNotAllowed()
    {
        await using var host = await TestApiHost.CreateAsync([
            () => new PlanDecision(PlanDecisionType.ToolCall, "need tool", null, [new ToolCallPlan("echo_context", """{"text":"hello"}""")], "test-model")
        ]);
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BestAgentDbContext>();
            var definition = await dbContext.AgentDefinitions.SingleAsync(item => item.Code == AgentDefinition.DefaultAgentCode);
            definition.AllowedToolsJson = "[]";
            await dbContext.SaveChangesAsync();
        }

        var client = host.Client;
        var response = await client.PostAsJsonAsync("/agent-runs", CreateRequest("idem-not-allowed", "hello"));
        var payload = await response.Content.ReadFromJsonAsync<AgentRunResponse>();

        response.EnsureSuccessStatusCode();
        Assert.Equal("Failed", payload?.Status);
        Assert.Contains("not allowed", payload?.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoredRecords_ShouldContainAuditFieldsAndDeletedFalse()
    {
        await using var host = await TestApiHost.CreateAsync([
            () => new PlanDecision(PlanDecisionType.Respond, "enough", "done", [], "test-model")
        ]);
        var client = host.Client;

        var response = await client.PostAsJsonAsync("/agent-runs", CreateRequest("idem-audit", "hello"));
        var payload = await response.Content.ReadFromJsonAsync<AgentRunResponse>();

        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BestAgentDbContext>();
        var run = await dbContext.AgentRuns.SingleAsync(item => item.RunId == payload!.RunId);

        Assert.Equal("system", run.Creator);
        Assert.Equal("system", run.LastModifier);
        Assert.False(run.Deleted);
    }

    [Fact]
    public async Task ResumeEndpoint_ShouldReturnBadRequest_ForCompletedRun()
    {
        await using var host = await TestApiHost.CreateAsync([
            () => new PlanDecision(PlanDecisionType.Respond, "enough", "done", [], "test-model")
        ]);
        var client = host.Client;

        var createResponse = await client.PostAsJsonAsync("/agent-runs", CreateRequest("idem-resume", "hello"));
        var created = await createResponse.Content.ReadFromJsonAsync<AgentRunResponse>();

        var response = await client.PostAsJsonAsync($"/agent-runs/{created!.RunId}:resume", new ResumeAgentRunRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static CreateAgentRunRequest CreateRequest(string idempotencyKey, string text)
    {
        return new CreateAgentRunRequest
        {
            AgentCode = AgentDefinition.DefaultAgentCode,
            SessionId = "session-1",
            UserId = "user-1",
            IdempotencyKey = idempotencyKey,
            Input = new AgentRunInputRequest
            {
                Text = text
            }
        };
    }
}
