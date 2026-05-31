using System.Collections.Concurrent;
using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
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
                case ApproveAgentRunStepMessage approve:
                    await HandleApproveAsync(
                        approve.RunId,
                        approve.StepId,
                        approve.ApproverId,
                        approve.ApproverName,
                        approve.ApproverRole,
                        approve.Comment,
                        stoppingToken);
                    break;
                case RejectAgentRunStepMessage reject:
                    await HandleRejectAsync(
                        reject.RunId,
                        reject.StepId,
                        reject.Comment,
                        reject.ApproverId,
                        reject.ApproverName,
                        reject.ApproverRole,
                        stoppingToken);
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
        var toolDefinitionRepository = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();

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
            modelGateway, stepDecisionParser, toolExecutor, agentStepRepo, toolDefinitionRepository,
            stoppingToken,
            evt => _eventBus.Publish(evt));

        await ApplyLoopResult(agentRunRepo, agentApprovalRepository, agentRun, loopResult);
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
        var toolDefinitionRepository = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();

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
            modelGateway, stepDecisionParser, toolExecutor, agentStepRepo, toolDefinitionRepository,
            stoppingToken,
            evt => _eventBus.Publish(evt));

        await ApplyLoopResult(agentRunRepo, agentApprovalRepository, agentRun, loopResult);
    }

    private async Task HandleApproveAsync(
        string runId,
        string stepId,
        string? approverId,
        string? approverName,
        string? approverRole,
        string? comment,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentDefRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var stepDecisionParser = scope.ServiceProvider.GetRequiredService<IStepDecisionParser>();
        var toolExecutor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var modelGateway = scope.ServiceProvider.GetRequiredService<IModelGateway>();
        var toolDefinitionRepository = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;

        var resolvedDefinition = await agentDefRepo.GetEnabledByCodeAsync(agentRun.AgentCode, stoppingToken);
        if (resolvedDefinition is null)
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Agent definition not found.", stoppingToken);
            return;
        }

        var pendingStep = await agentStepRepo.GetLastByRunIdAsync(runId, stoppingToken);
        if (pendingStep is null || pendingStep.StepId != stepId || pendingStep.Status != "Pending")
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Approval step is missing or no longer pending.", stoppingToken);
            return;
        }

        var approvalContext = ApprovalPayloadSerializer.Parse(pendingStep.DecisionPayload);
        var toolStartedAt = DateTime.UtcNow;
        var toolResult = await toolExecutor.ExecuteAsync(
            approvalContext.ToolName,
            approvalContext.ToolInput,
            new ToolExecutionContext(agentRun.RunId, agentRun.AgentCode, agentRun.InputPayload ?? string.Empty),
            stoppingToken);
        var toolEndedAt = DateTime.UtcNow;

        var approvedPayload = ApprovalPayloadSerializer.MarkApproved(approvalContext, comment);
        var approvalRecord = await agentApprovalRepository.GetByRunIdAndStepIdAsync(runId, stepId, stoppingToken);
        if (approvalRecord is not null)
        {
            approvalRecord = approvalRecord with
            {
                Decision = ApprovalDecisions.Approved,
                ApproverId = approverId?.Trim() ?? string.Empty,
                ApproverName = approverName?.Trim() ?? string.Empty,
                ApproverRole = approverRole?.Trim() ?? string.Empty,
                Comment = comment?.Trim() ?? string.Empty,
                DecidedAt = approvedPayload.DecidedAt,
                LastModifier = approverId?.Trim() ?? "system",
                LastModifierName = approverName?.Trim() ?? approverId?.Trim() ?? "system",
                LastModifyTime = approvedPayload.DecidedAt ?? toolEndedAt
            };
            await agentApprovalRepository.UpdateAsync(approvalRecord, stoppingToken);
        }

        if (toolResult.IsPending)
        {
            pendingStep = pendingStep with
            {
                DecisionPayload = ApprovalPayloadSerializer.Serialize(approvedPayload),
                LastModifyTime = toolEndedAt
            };
            await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

            agentRun = agentRun with
            {
                Status = "WaitingTool",
                CurrentWaitToken = toolResult.WaitToken ?? Guid.NewGuid().ToString("N"),
                CurrentStepNo = pendingStep.StepNo,
                StatusVersion = agentRun.StatusVersion + 1,
                LastModifyTime = toolEndedAt
            };
            await agentRunRepo.UpdateAsync(agentRun, stoppingToken);
            _eventBus.Publish(new AgentRunEvent(runId, "waiting", new AgentRunEventData(pendingStep.StepNo, "tool_call", "Pending")));
            return;
        }

        pendingStep = pendingStep with
        {
            Status = "Completed",
            OutputPayload = toolResult.Output,
            DecisionPayload = ApprovalPayloadSerializer.Serialize(approvedPayload),
            StartedAt = toolStartedAt,
            EndedAt = toolEndedAt,
            DurationMs = Math.Max(0, (long)(toolEndedAt - toolStartedAt).TotalMilliseconds),
            LastModifyTime = toolEndedAt
        };
        await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);
        _eventBus.Publish(new AgentRunEvent(runId, "step",
            new AgentRunEventData(pendingStep.StepNo, "tool_call", "Completed", toolResult.Output)));

        var followUpInput = BuildToolFollowUpInput(agentRun.InputPayload ?? string.Empty, toolResult.Output ?? string.Empty);
        var nextStepNo = agentRun.CurrentStepNo + 1;
        var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, followUpInput, nextStepNo, 0);

        var loopResult = await AgentRunLoop.ExecuteAsync(
            loopContext, resolvedDefinition,
            modelGateway, stepDecisionParser, toolExecutor, agentStepRepo, toolDefinitionRepository,
            stoppingToken,
            evt => _eventBus.Publish(evt));

        await ApplyLoopResult(agentRunRepo, agentApprovalRepository, agentRun, loopResult);
    }

    private async Task HandleRejectAsync(
        string runId,
        string stepId,
        string? comment,
        string? approverId,
        string? approverName,
        string? approverRole,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;

        var pendingStep = await agentStepRepo.GetLastByRunIdAsync(runId, stoppingToken);
        if (pendingStep is null || pendingStep.StepId != stepId || pendingStep.Status != "Pending")
        {
            return;
        }

        var approvalPayload = ApprovalPayloadSerializer.Parse(pendingStep.DecisionPayload);
        var rejectedPayload = ApprovalPayloadSerializer.MarkRejected(approvalPayload, comment);
        var rejectedAt = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(comment) ? "Approval rejected." : comment.Trim();

        var approvalRecord = await agentApprovalRepository.GetByRunIdAndStepIdAsync(runId, stepId, stoppingToken);
        if (approvalRecord is not null)
        {
            approvalRecord = approvalRecord with
            {
                Decision = ApprovalDecisions.Rejected,
                ApproverId = approverId?.Trim() ?? string.Empty,
                ApproverName = approverName?.Trim() ?? string.Empty,
                ApproverRole = approverRole?.Trim() ?? string.Empty,
                Comment = reason,
                DecidedAt = rejectedPayload.DecidedAt,
                LastModifier = approverId?.Trim() ?? "system",
                LastModifierName = approverName?.Trim() ?? approverId?.Trim() ?? "system",
                LastModifyTime = rejectedPayload.DecidedAt ?? rejectedAt
            };
            await agentApprovalRepository.UpdateAsync(approvalRecord, stoppingToken);
        }

        pendingStep = pendingStep with
        {
            Status = "Failed",
            ErrorPayload = reason,
            DecisionPayload = ApprovalPayloadSerializer.Serialize(rejectedPayload),
            EndedAt = rejectedAt,
            LastModifyTime = rejectedAt
        };
        await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

        agentRun = agentRun with
        {
            Status = "Failed",
            CurrentWaitToken = string.Empty,
            InterruptReason = reason,
            EndedAt = rejectedAt,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = rejectedAt
        };
        await agentRunRepo.UpdateAsync(agentRun, stoppingToken);

        _eventBus.Publish(new AgentRunEvent(runId, "approval_rejected",
            new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: reason)));
        _eventBus.Publish(new AgentRunEvent(runId, "error",
            new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: reason)));
    }

    private async Task ApplyLoopResult(
        IAgentRunRepository agentRunRepo,
        IAgentApprovalRepository agentApprovalRepository,
        AgentRun agentRun,
        AgentLoopResult loopResult)
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

            case AgentLoopWaitingApproval waitingApproval:
                var approvalAt = DateTime.UtcNow;
                var approval = new AgentApproval
                {
                    ApprovalId = Guid.NewGuid().ToString("N"),
                    RunId = agentRun.RunId,
                    StepId = waitingApproval.StepId,
                    RequestedAction = waitingApproval.ToolName,
                    RiskLevel = waitingApproval.SideEffectLevel,
                    RequestPayload = waitingApproval.ToolInput,
                    Decision = ApprovalDecisions.Pending,
                    WaitToken = waitingApproval.WaitToken,
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = approvalAt,
                    LastModifyTime = approvalAt
                };
                await agentApprovalRepository.AddAsync(approval, default);

                agentRun = agentRun with
                {
                    Status = "WaitingApproval",
                    CurrentWaitToken = waitingApproval.WaitToken,
                    CurrentStepNo = waitingApproval.SuspendedAtStepNo,
                    StatusVersion = agentRun.StatusVersion + 1,
                    LastModifyTime = approvalAt
                };
                await agentRunRepo.UpdateAsync(agentRun, default);
                _eventBus.Publish(new AgentRunEvent(agentRun.RunId, "waiting_approval",
                    new AgentRunEventData(waitingApproval.SuspendedAtStepNo, "tool_call", "Pending", waitingApproval.ToolName)));
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
