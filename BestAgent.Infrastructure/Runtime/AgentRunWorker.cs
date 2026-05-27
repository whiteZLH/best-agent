using System.Collections.Concurrent;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BestAgent.Infrastructure.Runtime;

public class AgentRunWorker : BackgroundService
{
    private readonly IAgentRunChannel _channel;
    private readonly IAgentRunEventBus _eventBus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRunWorker> _logger;
    private readonly ConcurrentDictionary<string, byte> _activeRuns = new();

    public AgentRunWorker(
        IAgentRunChannel channel,
        IAgentRunEventBus eventBus,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRunWorker> logger)
    {
        _channel = channel;
        _eventBus = eventBus;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _channel.ReadAllAsync(stoppingToken))
        {
            if (!_activeRuns.TryAdd(message.RunId, 0))
            {
                _logger.LogWarning("Run {RunId} already active, skipping", message.RunId);
                continue;
            }

            _ = ProcessRunAsync(message, stoppingToken)
                .ContinueWith(_ => _activeRuns.TryRemove(message.RunId, out byte _), TaskScheduler.Default);
        }
    }

    private async Task ProcessRunAsync(AgentRunMessage message, CancellationToken stoppingToken)
    {
        try
        {
            switch (message)
            {
                case CreateAgentRunMessage create:
                    await HandleCreateAsync(create.RunId, stoppingToken);
                    break;
                case ResumeAgentRunMessage resume:
                    await HandleResumeAsync(resume.RunId, resume.WaitToken, resume.ToolResult, stoppingToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process agent run {RunId}", message.RunId);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
                var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
                var agentRun = await agentRunRepo.GetByRunIdAsync(message.RunId, stoppingToken);
                if (agentRun is not null)
                    await FailRun(agentRunRepo, agentStepRepo, agentRun, ex.Message, stoppingToken);
            }
            catch { /* best effort */ }

            _eventBus.Publish(new AgentRunEvent(message.RunId, "error",
                new AgentRunEventData(0, "failed", "Failed", Error: ex.Message[..Math.Min(ex.Message.Length, 256)])));
        }
    }

    private async Task HandleCreateAsync(string runId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentDefRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var stepDecisionParser = scope.ServiceProvider.GetRequiredService<IStepDecisionParser>();
        var toolExecutor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var modelGateway = scope.ServiceProvider.GetRequiredService<IModelGateway>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;

        var resolvedDefinition = await agentDefRepo.GetEnabledByCodeAsync(agentRun.AgentCode, stoppingToken);
        if (resolvedDefinition is null)
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Agent definition not found.", stoppingToken);
            return;
        }

        var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, agentRun.InputPayload ?? string.Empty, 3, 0);

        var loopResult = await AgentRunLoop.ExecuteAsync(
            loopContext, resolvedDefinition,
            modelGateway, stepDecisionParser, toolExecutor, agentStepRepo,
            stoppingToken,
            evt => _eventBus.Publish(evt));

        await ApplyLoopResult(agentRunRepo, agentRun, loopResult);
    }

    private async Task HandleResumeAsync(string runId, string waitToken, string toolResult, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentDefRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var stepDecisionParser = scope.ServiceProvider.GetRequiredService<IStepDecisionParser>();
        var toolExecutor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var modelGateway = scope.ServiceProvider.GetRequiredService<IModelGateway>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;

        var resolvedDefinition = await agentDefRepo.GetEnabledByCodeAsync(agentRun.AgentCode, stoppingToken);
        if (resolvedDefinition is null)
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Agent definition not found.", stoppingToken);
            return;
        }

        var pendingStep = await agentStepRepo.GetLastByRunIdAsync(runId, stoppingToken);
        if (pendingStep is not null && pendingStep.Status == "Pending")
        {
            var completedAt = DateTime.UtcNow;
            pendingStep = pendingStep with
            {
                Status = "Completed",
                OutputPayload = toolResult,
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };
            await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);
            _eventBus.Publish(new AgentRunEvent(runId, "step",
                new AgentRunEventData(pendingStep.StepNo, "tool_call", "Completed", toolResult)));
        }

        var followUpInput = BuildToolFollowUpInput(agentRun.InputPayload ?? string.Empty, toolResult);
        var nextStepNo = agentRun.CurrentStepNo + 1;

        var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, followUpInput, nextStepNo, 0);

        var loopResult = await AgentRunLoop.ExecuteAsync(
            loopContext, resolvedDefinition,
            modelGateway, stepDecisionParser, toolExecutor, agentStepRepo,
            stoppingToken,
            evt => _eventBus.Publish(evt));

        await ApplyLoopResult(agentRunRepo, agentRun, loopResult);
    }

    private async Task ApplyLoopResult(IAgentRunRepository agentRunRepo, AgentRun agentRun, AgentLoopResult loopResult)
    {
        switch (loopResult)
        {
            case AgentLoopCompleted completed:
                var completedAt = DateTime.UtcNow;
                agentRun = agentRun with
                {
                    Status = "Completed",
                    OutputPayload = completed.Output,
                    EndedAt = completedAt,
                    StatusVersion = agentRun.StatusVersion + 1,
                    LastModifyTime = completedAt
                };
                await agentRunRepo.UpdateAsync(agentRun, default);
                _eventBus.Publish(new AgentRunEvent(agentRun.RunId, "done",
                    new AgentRunEventData(0, "completed", "Completed", completed.Output)));
                break;

            case AgentLoopSuspended suspended:
                var suspendedAt = DateTime.UtcNow;
                agentRun = agentRun with
                {
                    Status = "WaitingTool",
                    CurrentWaitToken = suspended.WaitToken,
                    CurrentStepNo = suspended.SuspendedAtStepNo,
                    StatusVersion = agentRun.StatusVersion + 1,
                    LastModifyTime = suspendedAt
                };
                await agentRunRepo.UpdateAsync(agentRun, default);
                _eventBus.Publish(new AgentRunEvent(agentRun.RunId, "waiting",
                    new AgentRunEventData(suspended.SuspendedAtStepNo, "tool_call", "Pending")));
                break;
        }
    }

    private static async Task FailRun(
        IAgentRunRepository runRepo, IAgentStepRepository stepRepo,
        AgentRun agentRun, string error, CancellationToken ct)
    {
        var failedAt = DateTime.UtcNow;
        var truncatedError = error[..Math.Min(error.Length, 256)];
        agentRun = agentRun with
        {
            Status = "Failed",
            InterruptReason = truncatedError,
            EndedAt = failedAt,
            LastModifyTime = failedAt
        };
        await runRepo.UpdateAsync(agentRun, ct);
        await stepRepo.AddAsync(AgentRunLoop.CreateStep(
            agentRun.RunId, agentRun.CurrentStepNo + 1, "failed", "Failed",
            agentRun.InputPayload, null, truncatedError, failedAt, failedAt), ct);
    }

    private static string BuildToolFollowUpInput(string originalInput, string toolResult)
    {
        return
            $"""
            Original user input:
            {originalInput}

            Tool result:
            {toolResult}

            Produce the final user-facing answer now.
            """;
    }
}
