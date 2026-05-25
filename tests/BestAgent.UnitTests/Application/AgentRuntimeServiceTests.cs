using BestAgent.Application.AgentRuns.Commands;
using BestAgent.Application.AgentRuns.Services;
using BestAgent.Application.Planning;
using BestAgent.Domain.Agents;

namespace BestAgent.UnitTests.Application;

public sealed class AgentRuntimeServiceTests
{
    private static AgentRuntimeService CreateService(InMemoryRuntimeDependencies dependencies)
    {
        return new AgentRuntimeService(
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            new ContextBuilder(),
            new PlanValidator());
    }

    [Fact]
    public async Task CreateHandler_ShouldReuseExistingRun_ForSameIdempotencyKey()
    {
        var dependencies = new InMemoryRuntimeDependencies();
        dependencies.Definitions.Add(new AgentDefinition());
        dependencies.QueuePlan(new PlanDecision(PlanDecisionType.Respond, "direct", "done", [], "test-model"));

        var handler = new CreateAgentRunCommandHandler(CreateService(dependencies));
        var command = new CreateAgentRunCommand("support-main", "session-1", "user-1", "idem-1", "hello");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.RunId, second.RunId);
        Assert.Single(dependencies.Runs);
    }

    [Fact]
    public async Task CreateFlow_ShouldWriteAuditFields_OnPersistedEntities()
    {
        var dependencies = new InMemoryRuntimeDependencies();
        dependencies.Definitions.Add(new AgentDefinition());
        dependencies.QueuePlan(new PlanDecision(PlanDecisionType.Respond, "direct", "done", [], "test-model"));

        var result = await CreateService(dependencies).CreateAsync(
            new RuntimeRequest("support-main", "session-1", "user-1", "idem-2", "hello"),
            CancellationToken.None);

        var run = dependencies.Runs.Single(item => item.RunId == result.RunId);
        var firstStep = dependencies.Steps.First();

        Assert.Equal("system", run.Creator);
        Assert.Equal("system", run.LastModifier);
        Assert.False(run.Deleted);
        Assert.Equal("system", firstStep.CreatorName);
        Assert.Equal("system", firstStep.LastModifierName);
        Assert.False(firstStep.Deleted);
    }

    [Fact]
    public async Task CreateFlow_ShouldFail_WhenToolIsNotAllowed()
    {
        var dependencies = new InMemoryRuntimeDependencies();
        dependencies.Definitions.Add(new AgentDefinition { AllowedToolsJson = "[]" });
        dependencies.QueuePlan(new PlanDecision(
            PlanDecisionType.ToolCall,
            "needs tool",
            null,
            [new ToolCallPlan("echo_context", """{"text":"ping"}""")],
            "test-model"));

        var result = await CreateService(dependencies).CreateAsync(
            new RuntimeRequest("support-main", "session-1", "user-1", "idem-3", "echo this"),
            CancellationToken.None);

        Assert.Equal("Failed", result.Status);
        Assert.Contains("not allowed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resume_ShouldRejectCompletedRun()
    {
        var dependencies = new InMemoryRuntimeDependencies();
        dependencies.Definitions.Add(new AgentDefinition());
        dependencies.QueuePlan(new PlanDecision(PlanDecisionType.Respond, "direct", "done", [], "test-model"));

        var service = CreateService(dependencies);
        var created = await service.CreateAsync(
            new RuntimeRequest("support-main", "session-1", "user-1", "idem-4", "hello"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync(created.RunId, CancellationToken.None));
        Assert.Contains("cannot be resumed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
