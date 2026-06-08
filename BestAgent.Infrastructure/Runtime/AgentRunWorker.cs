using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.Observability;
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
    private const int DefaultMaxHandoffDepth = 3;
    private readonly IAgentRunChannel _channel;
    private readonly IAgentRunEventBus _eventBus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRunWorker> _logger;
    private readonly ApprovalTimeoutOptions _approvalTimeoutOptions;
    private readonly ApprovalPolicyOptions _approvalPolicyOptions;
    private readonly IAgentMetrics _agentMetrics;
    private readonly ConcurrentDictionary<string, byte> _activeRuns = new();

    public AgentRunWorker(
        IAgentRunChannel channel,
        IAgentRunEventBus eventBus,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRunWorker> logger,
        ApprovalTimeoutOptions? approvalTimeoutOptions = null,
        ApprovalPolicyOptions? approvalPolicyOptions = null,
        IAgentMetrics? agentMetrics = null)
    {
        _channel = channel;
        _eventBus = eventBus;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _approvalTimeoutOptions = approvalTimeoutOptions ?? new ApprovalTimeoutOptions();
        _approvalPolicyOptions = ApprovalPolicyOptionsNormalizer.Normalize(approvalPolicyOptions);
        _agentMetrics = agentMetrics ?? NullAgentMetrics.Instance;
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
        using var activity = AgentTracing.Source.StartActivity(AgentTracing.RunProcessActivityName, ActivityKind.Internal);
        activity?.SetTag("bestagent.run_id", message.RunId);
        activity?.SetTag("bestagent.message_type", message.GetType().Name);

        try
        {
            switch (message)
            {
                case CreateAgentRunMessage create:
                    await HandleCreateAsync(create.RunId, stoppingToken);
                    break;
                case ResumeAgentRunMessage resume:
                    await HandleResumeAsync(resume.RunId, resume.WaitToken, resume.ToolResult, resume.InvocationId, stoppingToken);
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
                case CompleteHumanAgentRunMessage completeHuman:
                    await HandleCompleteHumanAsync(
                        completeHuman.RunId,
                        completeHuman.StepId,
                        completeHuman.WaitToken,
                        completeHuman.HumanResult,
                        completeHuman.Comment,
                        completeHuman.Terminate,
                        completeHuman.HumanOperatorId,
                        completeHuman.HumanOperatorName,
                        completeHuman.HumanOperatorRole,
                        stoppingToken);
                    break;
                case ResumeParentHandoffMessage resumeParent:
                    await HandleResumeParentHandoffAsync(
                        resumeParent.RunId,
                        resumeParent.StepId,
                        resumeParent.WaitToken,
                        resumeParent.ChildRunId,
                        stoppingToken);
                    break;
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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

            await PublishEventAsync(
                message.RunId,
                "error",
                "Failed",
                new AgentRunEventData(0, "failed", "Failed", Error: ex.Message[..Math.Min(ex.Message.Length, 256)]),
                stoppingToken);
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
        var toolInvocationRepository = scope.ServiceProvider.GetRequiredService<IToolInvocationRepository>();
        var routeRuleRepository = scope.ServiceProvider.GetService<IRouteRuleRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();
        var runtimeContextComposer = scope.ServiceProvider.GetService<IRuntimeContextComposer>();
        var runtimeMemoryWriter = scope.ServiceProvider.GetService<IRuntimeMemoryWriter>();
        var toolOutputValidator = scope.ServiceProvider.GetService<IToolOutputValidator>();
        var agentOutputValidator = scope.ServiceProvider.GetService<IAgentOutputValidator>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;
        if (IsCancelled(agentRun)) return;

        var resolvedDefinition = await GetResolvedDefinitionForRunAsync(agentDefRepo, agentRunRepo, agentStepRepo, agentRun, stoppingToken);
        if (resolvedDefinition is null)
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Agent definition not found.", stoppingToken);
            return;
        }

        var startTurn = await CountCompletedModelTurnsAsync(agentStepRepo, runId, stoppingToken);
        var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, agentRun.InputPayload ?? string.Empty, 3, startTurn);

        var loopResult = await AgentRunLoop.ExecuteAsync(
            loopContext, resolvedDefinition,
            modelGateway, stepDecisionParser, toolExecutor, agentStepRepo, toolDefinitionRepository,
            toolInvocationRepository,
            stoppingToken,
            evt => PublishEventAsync(evt.RunId, evt.EventType, agentRun.Status, evt.Data, stoppingToken),
            runtimeContextComposer,
            ResolveApprovalPolicyOptions(resolvedDefinition),
            routeRuleRepository);

        await ApplyLoopResult(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepository,
            agentRun,
            resolvedDefinition,
            loopResult,
            runtimeMemoryWriter,
            agentOutputValidator,
            stoppingToken);
        var updatedRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (updatedRun is not null)
        {
            await ResumeParentRunIfNeededAsync(agentRunRepo, agentStepRepo, updatedRun, stoppingToken);
        }
    }

    private async Task HandleResumeAsync(string runId, string waitToken, string toolResult, string? invocationId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentDefRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var stepDecisionParser = scope.ServiceProvider.GetRequiredService<IStepDecisionParser>();
        var toolExecutor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var modelGateway = scope.ServiceProvider.GetRequiredService<IModelGateway>();
        var toolDefinitionRepository = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();
        var toolInvocationRepository = scope.ServiceProvider.GetRequiredService<IToolInvocationRepository>();
        var routeRuleRepository = scope.ServiceProvider.GetService<IRouteRuleRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();
        var runtimeContextComposer = scope.ServiceProvider.GetService<IRuntimeContextComposer>();
        var runtimeMemoryWriter = scope.ServiceProvider.GetService<IRuntimeMemoryWriter>();
        var toolOutputValidator = scope.ServiceProvider.GetService<IToolOutputValidator>();
        var agentOutputValidator = scope.ServiceProvider.GetService<IAgentOutputValidator>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;
        if (IsCancelled(agentRun)) return;

        var resolvedDefinition = await GetResolvedDefinitionForRunAsync(agentDefRepo, agentRunRepo, agentStepRepo, agentRun, stoppingToken);
        if (resolvedDefinition is null)
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Agent definition not found.", stoppingToken);
            return;
        }

        ToolInvocation? pendingInvocation = null;
        AgentStep? pendingStep = null;

        if (!string.IsNullOrWhiteSpace(invocationId))
        {
            pendingInvocation = await toolInvocationRepository.GetByInvocationIdAsync(invocationId, stoppingToken);
            if (pendingInvocation is null
                || !string.Equals(pendingInvocation.RunId, runId, StringComparison.Ordinal)
                || !string.Equals(pendingInvocation.Status, "Pending", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(pendingInvocation.CallbackToken, waitToken, StringComparison.Ordinal))
            {
                await FailRun(agentRunRepo, agentStepRepo, agentRun, "Tool invocation is missing or no longer pending.", stoppingToken);
                return;
            }

            pendingStep = await agentStepRepo.GetByStepIdAsync(pendingInvocation.StepId, stoppingToken);
            if (pendingStep is null
                || !string.Equals(pendingStep.RunId, runId, StringComparison.Ordinal)
                || !string.Equals(pendingStep.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                await FailRun(agentRunRepo, agentStepRepo, agentRun, "Tool invocation step is missing or no longer pending.", stoppingToken);
                return;
            }
        }
        else
        {
            pendingStep = await agentStepRepo.GetLastByRunIdAsync(runId, stoppingToken);
            if (pendingStep is not null && pendingStep.Status == "Pending")
            {
                pendingInvocation = await toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(
                    runId,
                    pendingStep.StepId,
                    stoppingToken);
            }
        }

        if (pendingStep is not null && pendingStep.Status == "Pending")
        {
            var completedAt = DateTime.UtcNow;
            var toolName = pendingInvocation?.ToolName ?? ExtractToolNameFromStep(pendingStep);
            var completedToolResult = TryParseCompletedToolResultEnvelope(toolName, toolResult, out var parsedToolResult)
                ? parsedToolResult
                : ToolExecutionResult.Completed(toolName, toolResult);

            if (completedToolResult.IsFailed)
            {
                var errorMessage = completedToolResult.Error ?? completedToolResult.Output;
                var compensationPolicy = await GetCompensationPolicyAsync(
                    toolDefinitionRepository,
                    toolName,
                    stoppingToken);
                var errorPayload = ToolFailurePayloadSerializer.Create(
                    toolName,
                    "execution",
                    errorMessage,
                    compensationPolicy);
                pendingStep = pendingStep with
                {
                    Status = "Failed",
                    ErrorPayload = errorPayload,
                    EndedAt = completedAt,
                    LastModifyTime = completedAt
                };
                await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

                if (pendingInvocation is not null)
                {
                    pendingInvocation = FailToolInvocation(pendingInvocation, errorPayload, completedAt);
                    await toolInvocationRepository.UpdateAsync(pendingInvocation, stoppingToken);
                }

                await PublishEventAsync(
                    runId,
                    "step",
                    agentRun.Status,
                    new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: errorPayload),
                    stoppingToken);
                if (ToolCompensationPolicyHelper.IsManual(compensationPolicy))
                {
                    await StartManualCompensationAsync(
                        agentRunRepo,
                        agentStepRepo,
                        agentRun,
                        pendingStep.StepNo + 1,
                        pendingStep.StepId,
                        pendingInvocation?.InvocationId ?? string.Empty,
                        toolName,
                        pendingStep.InputPayload,
                        errorPayload,
                        BuildManualCompensationComment(toolName),
                        stoppingToken);
                    return;
                }

                await FailRun(agentRunRepo, agentStepRepo, agentRun, errorMessage, stoppingToken, errorPayload);
                await PublishEventAsync(
                    runId,
                    "error",
                    "Failed",
                    new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: errorPayload),
                    stoppingToken);
                return;
            }

            var completedToolOutput = completedToolResult.Output;
            var validationError = await ValidateToolOutputAsync(
                toolDefinitionRepository,
                toolOutputValidator,
                toolName,
                completedToolOutput,
                stoppingToken);
            if (validationError is not null)
            {
                var errorPayload = ToolFailurePayloadSerializer.Create(
                    toolName,
                    "output_validation",
                    validationError.Message,
                    validationError.CompensationPolicy);
                pendingStep = pendingStep with
                {
                    Status = "Failed",
                    ErrorPayload = errorPayload,
                    EndedAt = completedAt,
                    LastModifyTime = completedAt
                };
                await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

                if (pendingInvocation is not null)
                {
                    pendingInvocation = FailToolInvocation(pendingInvocation, errorPayload, completedAt);
                    await toolInvocationRepository.UpdateAsync(pendingInvocation, stoppingToken);
                }

                await PublishEventAsync(
                    runId,
                    "step",
                    agentRun.Status,
                    new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: errorPayload),
                    stoppingToken);
                if (ToolCompensationPolicyHelper.IsManual(validationError.CompensationPolicy))
                {
                    await StartManualCompensationAsync(
                        agentRunRepo,
                        agentStepRepo,
                        agentRun,
                        pendingStep.StepNo + 1,
                        pendingStep.StepId,
                        pendingInvocation?.InvocationId ?? string.Empty,
                        toolName,
                        pendingStep.InputPayload,
                        errorPayload,
                        BuildManualCompensationComment(toolName),
                        stoppingToken);
                    return;
                }

                await FailRun(agentRunRepo, agentStepRepo, agentRun, validationError.Message, stoppingToken, errorPayload);
                await PublishEventAsync(
                    runId,
                    "error",
                    "Failed",
                    new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: errorPayload),
                    stoppingToken);
                return;
            }

            pendingStep = pendingStep with
            {
                Status = "Completed",
                OutputPayload = completedToolOutput,
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };
            await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

            if (pendingInvocation is not null)
            {
                pendingInvocation = CompleteToolInvocation(pendingInvocation, completedToolOutput, completedAt);
                await toolInvocationRepository.UpdateAsync(pendingInvocation, stoppingToken);
            }

            await PublishEventAsync(
                runId,
                "step",
                agentRun.Status,
                new AgentRunEventData(pendingStep.StepNo, "tool_call", "Completed", completedToolOutput),
                stoppingToken);
            var memoryPolicy = MemoryPolicy.Parse(resolvedDefinition.Version.MemoryPolicy);
            if (runtimeMemoryWriter is not null
                && (memoryPolicy.AllowsToolResultWrite(toolName) || memoryPolicy.AllowsUserMemoryWrite()))
            {
                await runtimeMemoryWriter.RecordToolResultAsync(
                    agentRun,
                    toolName,
                    pendingStep.InputPayload,
                    completedToolOutput,
                    memoryPolicy.AllowsToolResultWrite(toolName),
                    memoryPolicy.AllowsUserMemoryWrite(),
                    stoppingToken);
            }

            toolResult = completedToolOutput;
        }

        var followUpInput = BuildToolFollowUpInput(agentRun.InputPayload ?? string.Empty, toolResult);
        var nextStepNo = agentRun.CurrentStepNo + 1;

        var startTurn = await CountCompletedModelTurnsAsync(agentStepRepo, runId, stoppingToken);
        var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, followUpInput, nextStepNo, startTurn);

        var loopResult = await AgentRunLoop.ExecuteAsync(
            loopContext, resolvedDefinition,
            modelGateway, stepDecisionParser, toolExecutor, agentStepRepo, toolDefinitionRepository,
            toolInvocationRepository,
            stoppingToken,
            evt => PublishEventAsync(evt.RunId, evt.EventType, agentRun.Status, evt.Data, stoppingToken),
            runtimeContextComposer,
            ResolveApprovalPolicyOptions(resolvedDefinition),
            routeRuleRepository);

        await ApplyLoopResult(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepository,
            agentRun,
            resolvedDefinition,
            loopResult,
            runtimeMemoryWriter,
            agentOutputValidator,
            stoppingToken);
        var updatedRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (updatedRun is not null)
        {
            await ResumeParentRunIfNeededAsync(agentRunRepo, agentStepRepo, updatedRun, stoppingToken);
        }
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
        using var activity = AgentTracing.Source.StartActivity(AgentTracing.ApprovalActivityName, ActivityKind.Internal);
        activity?.SetTag("bestagent.run_id", runId);
        activity?.SetTag("bestagent.step_id", stepId);
        activity?.SetTag("bestagent.approval_action", "approve");
        activity?.SetTag("bestagent.approver_role", approverRole);

        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentDefRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var stepDecisionParser = scope.ServiceProvider.GetRequiredService<IStepDecisionParser>();
        var toolExecutor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var modelGateway = scope.ServiceProvider.GetRequiredService<IModelGateway>();
        var toolDefinitionRepository = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();
        var toolInvocationRepository = scope.ServiceProvider.GetRequiredService<IToolInvocationRepository>();
        var routeRuleRepository = scope.ServiceProvider.GetService<IRouteRuleRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();
        var runtimeContextComposer = scope.ServiceProvider.GetService<IRuntimeContextComposer>();
        var runtimeMemoryWriter = scope.ServiceProvider.GetService<IRuntimeMemoryWriter>();
        var toolOutputValidator = scope.ServiceProvider.GetService<IToolOutputValidator>();
        var agentOutputValidator = scope.ServiceProvider.GetService<IAgentOutputValidator>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;
        if (IsCancelled(agentRun)) return;

        var resolvedDefinition = await GetResolvedDefinitionForRunAsync(agentDefRepo, agentRunRepo, agentStepRepo, agentRun, stoppingToken);
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

        var approvalRecord = await agentApprovalRepository.GetByRunIdAndStepIdAsync(runId, stepId, stoppingToken);
        if (string.Equals(pendingStep.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
            && HandoffPayloadSerializer.TryParse(pendingStep.DecisionPayload, out var handoffPayload)
            && handoffPayload!.ApprovalRequired
            && string.Equals(handoffPayload.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
        {
            var approvedPayload = HandoffPayloadSerializer.MarkApproved(handoffPayload, comment);
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
                    LastModifyTime = approvedPayload.DecidedAt ?? DateTime.UtcNow
                };
                await agentApprovalRepository.UpdateAsync(approvalRecord, stoppingToken);
                RecordApprovalWaitCompleted(
                    agentRun.AgentCode,
                    pendingStep.StepType,
                    ApprovalDecisions.Approved,
                    approvalRecord.CreateTime,
                    approvedPayload.DecidedAt ?? DateTime.UtcNow);
            }

            pendingStep = pendingStep with
            {
                DecisionPayload = HandoffPayloadSerializer.Serialize(approvedPayload),
                LastModifyTime = approvedPayload.DecidedAt ?? DateTime.UtcNow
            };
            await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

            await StartHandoffAsync(
                agentRunRepo,
                agentStepRepo,
                resolvedDefinition,
                agentRun,
                new AgentLoopWaitingHandoff(
                    approvedPayload.WaitToken,
                    pendingStep.StepNo,
                    pendingStep.StepId,
                    approvedPayload.TargetAgent,
                    approvedPayload.HandoffInput,
                    approvedPayload.Mode,
                    approvedPayload.ChildRunId),
                stoppingToken);
            activity?.SetTag("bestagent.status", "approved");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return;
        }

        var approvalContext = ApprovalPayloadSerializer.Parse(pendingStep.DecisionPayload);
        var approvedToolPayload = ApprovalPayloadSerializer.MarkApproved(approvalContext, comment);
        if (approvalRecord is not null)
        {
            approvalRecord = approvalRecord with
            {
                Decision = ApprovalDecisions.Approved,
                ApproverId = approverId?.Trim() ?? string.Empty,
                ApproverName = approverName?.Trim() ?? string.Empty,
                ApproverRole = approverRole?.Trim() ?? string.Empty,
                Comment = comment?.Trim() ?? string.Empty,
                DecidedAt = approvedToolPayload.DecidedAt,
                LastModifier = approverId?.Trim() ?? "system",
                LastModifierName = approverName?.Trim() ?? approverId?.Trim() ?? "system",
                LastModifyTime = approvedToolPayload.DecidedAt ?? DateTime.UtcNow
            };
            await agentApprovalRepository.UpdateAsync(approvalRecord, stoppingToken);
            RecordApprovalWaitCompleted(
                agentRun.AgentCode,
                pendingStep.StepType,
                ApprovalDecisions.Approved,
                approvalRecord.CreateTime,
                approvedToolPayload.DecidedAt ?? DateTime.UtcNow);
        }

        if (string.Equals(pendingStep.StepType, "approval_request", StringComparison.OrdinalIgnoreCase))
        {
            var completedAt = approvedToolPayload.DecidedAt ?? DateTime.UtcNow;
            pendingStep = pendingStep with
            {
                Status = "Completed",
                DecisionPayload = ApprovalPayloadSerializer.Serialize(approvedToolPayload),
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };
            await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

            await PublishEventAsync(
                runId,
                "step",
                agentRun.Status,
                new AgentRunEventData(
                    pendingStep.StepNo,
                    pendingStep.StepType,
                    "Completed",
                    approvalContext.ToolName,
                    DecisionPayload: pendingStep.DecisionPayload),
                stoppingToken);

            var approvalFollowUpInput = AgentRunLoop.BuildApprovalFollowUpInput(
                agentRun.InputPayload ?? string.Empty,
                approvalContext.ToolName,
                approvalContext.ToolInput,
                approvalContext.SideEffectLevel,
                approvedToolPayload.Comment);
            var approvalNextStepNo = agentRun.CurrentStepNo + 1;
            var approvalStartTurn = await CountCompletedModelTurnsAsync(agentStepRepo, runId, stoppingToken);
            var approvalLoopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, approvalFollowUpInput, approvalNextStepNo, approvalStartTurn);

            var approvalLoopResult = await AgentRunLoop.ExecuteAsync(
                approvalLoopContext,
                resolvedDefinition,
                modelGateway,
                stepDecisionParser,
                toolExecutor,
                agentStepRepo,
                toolDefinitionRepository,
                toolInvocationRepository,
                stoppingToken,
                evt => PublishEventAsync(evt.RunId, evt.EventType, agentRun.Status, evt.Data, stoppingToken),
                runtimeContextComposer,
                ResolveApprovalPolicyOptions(resolvedDefinition),
                routeRuleRepository);

            await ApplyLoopResult(
                agentRunRepo,
                agentStepRepo,
                agentApprovalRepository,
                agentRun,
                resolvedDefinition,
                approvalLoopResult,
                runtimeMemoryWriter,
                agentOutputValidator,
                stoppingToken);
            activity?.SetTag("bestagent.status", "approved");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return;
        }

        var toolStartedAt = DateTime.UtcNow;
        ToolExecutionResult toolResult;
        try
        {
            toolResult = await toolExecutor.ExecuteAsync(
                approvalContext.ToolName,
                approvalContext.ToolInput,
                new ToolExecutionContext(agentRun.RunId, agentRun.AgentCode, agentRun.InputPayload ?? string.Empty),
                stoppingToken);
        }
        catch (InvalidOperationException ex)
        {
            var failedAt = DateTime.UtcNow;
            var compensationPolicy = await GetCompensationPolicyAsync(
                toolDefinitionRepository,
                approvalContext.ToolName,
                stoppingToken);
            var errorPayload = ToolFailurePayloadSerializer.Create(
                approvalContext.ToolName,
                "execution",
                ex.Message,
                compensationPolicy);
            var failedInvocationId = await FailApprovedToolStepAsync(
                agentRunRepo,
                agentStepRepo,
                toolInvocationRepository,
                agentRun,
                pendingStep,
                approvedToolPayload,
                approvalContext.ToolName,
                approvalContext.ToolInput,
                errorPayload,
                ex.Message,
                toolStartedAt,
                failedAt,
                stoppingToken);
            await PublishEventAsync(
                runId,
                "step",
                "Failed",
                new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: errorPayload),
                stoppingToken);
            if (ToolCompensationPolicyHelper.IsManual(compensationPolicy))
            {
                await StartManualCompensationAsync(
                    agentRunRepo,
                    agentStepRepo,
                    agentRun,
                    pendingStep.StepNo + 1,
                    pendingStep.StepId,
                    failedInvocationId,
                    approvalContext.ToolName,
                    approvalContext.ToolInput,
                    errorPayload,
                    BuildManualCompensationComment(approvalContext.ToolName),
                    stoppingToken);
                return;
            }

            await PublishEventAsync(
                runId,
                "error",
                "Failed",
                new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: errorPayload),
                stoppingToken);
            return;
        }
        var toolEndedAt = DateTime.UtcNow;

        if (toolResult.IsFailed)
        {
            var errorMessage = toolResult.Error ?? toolResult.Output;
            var compensationPolicy = await GetCompensationPolicyAsync(
                toolDefinitionRepository,
                approvalContext.ToolName,
                stoppingToken);
            var errorPayload = ToolFailurePayloadSerializer.Create(
                approvalContext.ToolName,
                "execution",
                errorMessage,
                compensationPolicy);
            var failedInvocationId = await FailApprovedToolStepAsync(
                agentRunRepo,
                agentStepRepo,
                toolInvocationRepository,
                agentRun,
                pendingStep,
                approvedToolPayload,
                approvalContext.ToolName,
                approvalContext.ToolInput,
                errorPayload,
                errorMessage,
                toolStartedAt,
                toolEndedAt,
                stoppingToken);
            await PublishEventAsync(
                runId,
                "step",
                "Failed",
                new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: errorPayload),
                stoppingToken);
            if (ToolCompensationPolicyHelper.IsManual(compensationPolicy))
            {
                await StartManualCompensationAsync(
                    agentRunRepo,
                    agentStepRepo,
                    agentRun,
                    pendingStep.StepNo + 1,
                    pendingStep.StepId,
                    failedInvocationId,
                    approvalContext.ToolName,
                    approvalContext.ToolInput,
                    errorPayload,
                    BuildManualCompensationComment(approvalContext.ToolName),
                    stoppingToken);
                return;
            }

            await PublishEventAsync(
                runId,
                "error",
                "Failed",
                new AgentRunEventData(pendingStep.StepNo, "tool_call", "Failed", Error: errorPayload),
                stoppingToken);
            return;
        }

        if (toolResult.IsPending)
        {
            var waitToken = toolResult.WaitToken ?? Guid.NewGuid().ToString("N");
            var invocationId = Guid.NewGuid().ToString("N");
            await toolInvocationRepository.AddAsync(
                AgentRunLoop.CreateToolInvocation(
                    invocationId,
                    runId,
                    pendingStep.StepId,
                    approvalContext.ToolName,
                    "async",
                    "Pending",
                    approvalContext.ToolInput,
                    null,
                    null,
                    invocationId,
                    waitToken,
                    toolStartedAt,
                    null,
                    toolEndedAt),
                stoppingToken);

            pendingStep = pendingStep with
            {
                DecisionPayload = ApprovalPayloadSerializer.Serialize(approvedToolPayload),
                LastModifyTime = toolEndedAt
            };
            await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

            agentRun = agentRun with
            {
                Status = "WaitingTool",
                CurrentWaitToken = waitToken,
                CurrentStepNo = pendingStep.StepNo,
                StatusVersion = agentRun.StatusVersion + 1,
                LastModifyTime = toolEndedAt
            };
            await agentRunRepo.UpdateAsync(agentRun, stoppingToken);
            await PublishEventAsync(
                runId,
                "waiting",
                "WaitingTool",
                new AgentRunEventData(pendingStep.StepNo, "tool_call", "Pending"),
                stoppingToken);
            return;
        }

        pendingStep = pendingStep with
        {
            Status = "Completed",
            OutputPayload = toolResult.Output,
            DecisionPayload = ApprovalPayloadSerializer.Serialize(approvedToolPayload),
            StartedAt = toolStartedAt,
            EndedAt = toolEndedAt,
            DurationMs = Math.Max(0, (long)(toolEndedAt - toolStartedAt).TotalMilliseconds),
            LastModifyTime = toolEndedAt
        };
        await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

        var completedInvocationId = Guid.NewGuid().ToString("N");
        await toolInvocationRepository.AddAsync(
            AgentRunLoop.CreateToolInvocation(
                completedInvocationId,
                runId,
                pendingStep.StepId,
                approvalContext.ToolName,
                "sync",
                "Completed",
                approvalContext.ToolInput,
                toolResult.Output,
                null,
                completedInvocationId,
                string.Empty,
                toolStartedAt,
                toolEndedAt,
                toolEndedAt),
            stoppingToken);

        await PublishEventAsync(
            runId,
            "step",
            agentRun.Status,
            new AgentRunEventData(pendingStep.StepNo, "tool_call", "Completed", toolResult.Output),
            stoppingToken);
        var memoryPolicy = MemoryPolicy.Parse(resolvedDefinition.Version.MemoryPolicy);
        if (runtimeMemoryWriter is not null
            && (memoryPolicy.AllowsToolResultWrite(approvalContext.ToolName) || memoryPolicy.AllowsUserMemoryWrite()))
        {
            await runtimeMemoryWriter.RecordToolResultAsync(
                agentRun,
                approvalContext.ToolName,
                approvalContext.ToolInput,
                toolResult.Output,
                memoryPolicy.AllowsToolResultWrite(approvalContext.ToolName),
                memoryPolicy.AllowsUserMemoryWrite(),
                stoppingToken);
        }

        var followUpInput = BuildToolFollowUpInput(agentRun.InputPayload ?? string.Empty, toolResult.Output ?? string.Empty);
        var nextStepNo = agentRun.CurrentStepNo + 1;
        var startTurn = await CountCompletedModelTurnsAsync(agentStepRepo, runId, stoppingToken);
        var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, followUpInput, nextStepNo, startTurn);

        var loopResult = await AgentRunLoop.ExecuteAsync(
            loopContext, resolvedDefinition,
            modelGateway, stepDecisionParser, toolExecutor, agentStepRepo, toolDefinitionRepository,
            toolInvocationRepository,
            stoppingToken,
            evt => PublishEventAsync(evt.RunId, evt.EventType, agentRun.Status, evt.Data, stoppingToken),
            runtimeContextComposer,
            ResolveApprovalPolicyOptions(resolvedDefinition),
            routeRuleRepository);

        await ApplyLoopResult(
            agentRunRepo,
            agentStepRepo,
            agentApprovalRepository,
            agentRun,
            resolvedDefinition,
            loopResult,
            runtimeMemoryWriter,
            agentOutputValidator,
            stoppingToken);
        activity?.SetTag("bestagent.status", "approved");
        activity?.SetStatus(ActivityStatusCode.Ok);
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
        using var activity = AgentTracing.Source.StartActivity(AgentTracing.ApprovalActivityName, ActivityKind.Internal);
        activity?.SetTag("bestagent.run_id", runId);
        activity?.SetTag("bestagent.step_id", stepId);
        activity?.SetTag("bestagent.approval_action", "reject");
        activity?.SetTag("bestagent.approver_role", approverRole);

        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;
        if (IsCancelled(agentRun)) return;

        var pendingStep = await agentStepRepo.GetLastByRunIdAsync(runId, stoppingToken);
        if (pendingStep is null || pendingStep.StepId != stepId || pendingStep.Status != "Pending")
        {
            return;
        }

        var rejectedAt = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(comment) ? "Approval rejected." : comment.Trim();
        var approvalRecord = await agentApprovalRepository.GetByRunIdAndStepIdAsync(runId, stepId, stoppingToken);

        string serializedRejectedPayload;
        if (string.Equals(pendingStep.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
            && HandoffPayloadSerializer.TryParse(pendingStep.DecisionPayload, out var handoffPayload)
            && handoffPayload!.ApprovalRequired
            && string.Equals(handoffPayload.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
        {
            serializedRejectedPayload = HandoffPayloadSerializer.Serialize(
                HandoffPayloadSerializer.MarkRejected(handoffPayload, reason));
        }
        else
        {
            var approvalPayload = ApprovalPayloadSerializer.Parse(pendingStep.DecisionPayload);
            serializedRejectedPayload = ApprovalPayloadSerializer.Serialize(
                ApprovalPayloadSerializer.MarkRejected(approvalPayload, reason));
        }

        if (approvalRecord is not null)
        {
            approvalRecord = approvalRecord with
            {
                Decision = ApprovalDecisions.Rejected,
                ApproverId = approverId?.Trim() ?? string.Empty,
                ApproverName = approverName?.Trim() ?? string.Empty,
                ApproverRole = approverRole?.Trim() ?? string.Empty,
                Comment = reason,
                DecidedAt = rejectedAt,
                LastModifier = approverId?.Trim() ?? "system",
                LastModifierName = approverName?.Trim() ?? approverId?.Trim() ?? "system",
                LastModifyTime = rejectedAt
            };
            await agentApprovalRepository.UpdateAsync(approvalRecord, stoppingToken);
            RecordApprovalWaitCompleted(
                agentRun.AgentCode,
                pendingStep.StepType,
                ApprovalDecisions.Rejected,
                approvalRecord.CreateTime,
                rejectedAt);
        }

        pendingStep = pendingStep with
        {
            Status = "Failed",
            ErrorPayload = reason,
            DecisionPayload = serializedRejectedPayload,
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
        RecordRunCompleted(agentRun);

        await PublishEventAsync(
            runId,
            "approval_rejected",
            "Failed",
            new AgentRunEventData(
                pendingStep.StepNo,
                pendingStep.StepType,
                "Failed",
                Error: reason,
                DecisionPayload: serializedRejectedPayload),
            stoppingToken);
        await PublishEventAsync(
            runId,
            "error",
            "Failed",
            new AgentRunEventData(
                pendingStep.StepNo,
                pendingStep.StepType,
                "Failed",
                Error: reason,
                DecisionPayload: serializedRejectedPayload),
            stoppingToken);
        activity?.SetTag("bestagent.status", "rejected");
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private async Task HandleCompleteHumanAsync(
        string runId,
        string stepId,
        string waitToken,
        string? humanResult,
        string? comment,
        bool terminate,
        string? humanOperatorId,
        string? humanOperatorName,
        string? humanOperatorRole,
        CancellationToken stoppingToken)
    {
        using var activity = AgentTracing.Source.StartActivity(AgentTracing.ApprovalActivityName, ActivityKind.Internal);
        activity?.SetTag("bestagent.run_id", runId);
        activity?.SetTag("bestagent.step_id", stepId);
        activity?.SetTag("bestagent.approval_action", terminate ? "human_terminate" : "human_complete");
        activity?.SetTag("bestagent.approver_role", humanOperatorRole);

        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();
        var agentDefRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var stepDecisionParser = scope.ServiceProvider.GetRequiredService<IStepDecisionParser>();
        var toolExecutor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var modelGateway = scope.ServiceProvider.GetRequiredService<IModelGateway>();
        var toolDefinitionRepository = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();
        var toolInvocationRepository = scope.ServiceProvider.GetRequiredService<IToolInvocationRepository>();
        var routeRuleRepository = scope.ServiceProvider.GetService<IRouteRuleRepository>();
        var runtimeContextComposer = scope.ServiceProvider.GetService<IRuntimeContextComposer>();
        var runtimeMemoryWriter = scope.ServiceProvider.GetService<IRuntimeMemoryWriter>();
        var toolOutputValidator = scope.ServiceProvider.GetService<IToolOutputValidator>();
        var agentOutputValidator = scope.ServiceProvider.GetService<IAgentOutputValidator>();

        var agentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (agentRun is null) return;
        if (IsCancelled(agentRun)) return;

        var pendingStep = await agentStepRepo.GetLastByRunIdAsync(runId, stoppingToken);
        if (pendingStep is null
            || !string.Equals(pendingStep.StepId, stepId, StringComparison.Ordinal)
            || !string.Equals(pendingStep.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Human wait step is missing or no longer pending.", stoppingToken);
            return;
        }

        if (!HumanApprovalPayloadSerializer.TryParse(pendingStep.DecisionPayload, out var humanPayload)
            || !string.Equals(humanPayload!.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Human wait step payload is invalid.", stoppingToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(waitToken)
            && !string.IsNullOrWhiteSpace(agentRun.CurrentWaitToken)
            && !string.Equals(agentRun.CurrentWaitToken, waitToken, StringComparison.Ordinal))
        {
            await FailRun(agentRunRepo, agentStepRepo, agentRun, "Human wait token mismatch.", stoppingToken);
            return;
        }

        var completedAt = DateTime.UtcNow;
        if (terminate)
        {
            var terminatedPayload = HumanApprovalPayloadSerializer.MarkTerminated(
                humanPayload,
                comment,
                humanOperatorId,
                humanOperatorName,
                humanOperatorRole);
            var reason = terminatedPayload.Comment ?? "Terminated by human operator.";

            pendingStep = pendingStep with
            {
                Status = "Failed",
                ErrorPayload = reason,
                DecisionPayload = HumanApprovalPayloadSerializer.Serialize(terminatedPayload),
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };
            await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

            agentRun = agentRun with
            {
                Status = "Failed",
                CurrentWaitToken = string.Empty,
                InterruptReason = reason,
                EndedAt = completedAt,
                StatusVersion = agentRun.StatusVersion + 1,
                LastModifyTime = completedAt
            };
            await agentRunRepo.UpdateAsync(agentRun, stoppingToken);
            RecordRunCompleted(agentRun);

            await PublishEventAsync(
                runId,
                "step",
                "Failed",
                new AgentRunEventData(
                    pendingStep.StepNo,
                    pendingStep.StepType,
                    "Failed",
                    Error: reason,
                    DecisionPayload: pendingStep.DecisionPayload),
                stoppingToken);
            await PublishEventAsync(
                runId,
                "error",
                "Failed",
                new AgentRunEventData(
                    pendingStep.StepNo,
                    pendingStep.StepType,
                    "Failed",
                    Error: reason,
                    DecisionPayload: pendingStep.DecisionPayload),
                stoppingToken);
            activity?.SetTag("bestagent.status", "terminated");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return;
        }

        var completedPayload = HumanApprovalPayloadSerializer.MarkCompleted(
            humanPayload,
            humanResult,
            comment,
            humanOperatorId,
            humanOperatorName,
            humanOperatorRole);
        pendingStep = pendingStep with
        {
            Status = "Completed",
            OutputPayload = humanResult,
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(completedPayload),
            EndedAt = completedAt,
            LastModifyTime = completedAt
        };
        await agentStepRepo.UpdateAsync(pendingStep, stoppingToken);

        await PublishEventAsync(
            runId,
            "step",
            "Completed",
            new AgentRunEventData(
                pendingStep.StepNo,
                pendingStep.StepType,
                "Completed",
                humanResult,
                DecisionPayload: pendingStep.DecisionPayload),
            stoppingToken);

        if (completedPayload.ContinueAsToolResult
            && (string.Equals(completedPayload.SourceType, "tool_wait", StringComparison.OrdinalIgnoreCase)
                || string.Equals(completedPayload.SourceType, "tool_result", StringComparison.OrdinalIgnoreCase)
                || string.Equals(completedPayload.SourceType, "tool_failure", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(humanResult))
            {
                await FailRun(agentRunRepo, agentStepRepo, agentRun, "Human replacement result is required to continue tool flow.", stoppingToken);
                await PublishEventAsync(
                    runId,
                    "error",
                    "Failed",
                    new AgentRunEventData(pendingStep.StepNo, pendingStep.StepType, "Failed", Error: "Human replacement result is required to continue tool flow."),
                    stoppingToken);
                return;
            }

            var resolvedDefinition = await GetResolvedDefinitionForRunAsync(agentDefRepo, agentRunRepo, agentStepRepo, agentRun, stoppingToken);
            if (resolvedDefinition is null)
            {
                await FailRun(agentRunRepo, agentStepRepo, agentRun, "Agent definition not found.", stoppingToken);
                await PublishEventAsync(
                    runId,
                    "error",
                    "Failed",
                    new AgentRunEventData(pendingStep.StepNo, pendingStep.StepType, "Failed", Error: "Agent definition not found."),
                    stoppingToken);
                return;
            }

            var validationError = await ValidateToolOutputAsync(
                toolDefinitionRepository,
                toolOutputValidator,
                completedPayload.SourceToolName,
                humanResult,
                stoppingToken);
            if (validationError is not null)
            {
                var sourceToolName = string.IsNullOrWhiteSpace(completedPayload.SourceToolName)
                    ? "tool_call"
                    : completedPayload.SourceToolName;
                var errorPayload = ToolFailurePayloadSerializer.Create(
                    sourceToolName,
                    "output_validation",
                    validationError.Message,
                    validationError.CompensationPolicy);
                if (ToolCompensationPolicyHelper.IsManual(validationError.CompensationPolicy))
                {
                    var nextHumanStepNo = pendingStep.StepNo + 1;
                    await StartManualCompensationAsync(
                        agentRunRepo,
                        agentStepRepo,
                        agentRun,
                        nextHumanStepNo,
                        pendingStep.StepId,
                        completedPayload.SourceInvocationId ?? string.Empty,
                        sourceToolName,
                        completedPayload.SourceToolInput,
                        errorPayload,
                        BuildManualCompensationComment(sourceToolName),
                        stoppingToken);
                    return;
                }

                await FailRun(agentRunRepo, agentStepRepo, agentRun, validationError.Message, stoppingToken, errorPayload);
                await PublishEventAsync(
                    runId,
                    "error",
                    "Failed",
                    new AgentRunEventData(pendingStep.StepNo, pendingStep.StepType, "Failed", Error: errorPayload),
                    stoppingToken);
                return;
            }

            var memoryPolicy = MemoryPolicy.Parse(resolvedDefinition.Version.MemoryPolicy);
            if (runtimeMemoryWriter is not null
                && (memoryPolicy.AllowsToolResultWrite(completedPayload.SourceToolName) || memoryPolicy.AllowsUserMemoryWrite()))
            {
                await runtimeMemoryWriter.RecordToolResultAsync(
                    agentRun,
                    string.IsNullOrWhiteSpace(completedPayload.SourceToolName) ? "tool_call" : completedPayload.SourceToolName,
                    completedPayload.SourceToolInput,
                    humanResult,
                    memoryPolicy.AllowsToolResultWrite(completedPayload.SourceToolName),
                    memoryPolicy.AllowsUserMemoryWrite(),
                    stoppingToken);
            }

            var followUpInput = BuildToolFollowUpInput(
                agentRun.InputPayload ?? string.Empty,
                humanResult,
                completedPayload.SourceToolName,
                isHumanReplacement: true,
                originalToolResult: completedPayload.SourceToolOutput,
                replacementReason: completedPayload.Comment);
            var nextStepNo = agentRun.CurrentStepNo + 1;
            var startTurn = await CountCompletedModelTurnsAsync(agentStepRepo, runId, stoppingToken);
            var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, followUpInput, nextStepNo, startTurn);

            var loopResult = await AgentRunLoop.ExecuteAsync(
                loopContext,
                resolvedDefinition,
                modelGateway,
                stepDecisionParser,
                toolExecutor,
                agentStepRepo,
                toolDefinitionRepository,
                toolInvocationRepository,
                stoppingToken,
                evt => PublishEventAsync(evt.RunId, evt.EventType, agentRun.Status, evt.Data, stoppingToken),
                runtimeContextComposer,
                ResolveApprovalPolicyOptions(resolvedDefinition),
                routeRuleRepository);

            await ApplyLoopResult(
                agentRunRepo,
                agentStepRepo,
                agentApprovalRepository,
                agentRun,
                resolvedDefinition,
                loopResult,
                runtimeMemoryWriter,
                agentOutputValidator,
                stoppingToken);
            return;
        }

        agentRun = agentRun with
        {
            Status = "Completed",
            CurrentWaitToken = string.Empty,
            OutputPayload = humanResult,
            InterruptReason = string.Empty,
            EndedAt = completedAt,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = completedAt
        };
        await agentRunRepo.UpdateAsync(agentRun, stoppingToken);
        RecordRunCompleted(agentRun);
        var terminalDefinition = await GetResolvedDefinitionForRunAsync(agentDefRepo, agentRunRepo, agentStepRepo, agentRun, stoppingToken);
        if (runtimeMemoryWriter is not null
            && terminalDefinition is not null
            && AllowsSummaryMemoryWrite(terminalDefinition))
        {
            await runtimeMemoryWriter.RecordRunCompletionSummaryAsync(agentRun, humanResult, stoppingToken);
        }

        await PublishEventAsync(
            runId,
            "done",
            "Completed",
            new AgentRunEventData(0, "completed", "Completed", humanResult),
            stoppingToken);
        activity?.SetTag("bestagent.status", "completed");
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private async Task HandleResumeParentHandoffAsync(
        string runId,
        string stepId,
        string waitToken,
        string childRunId,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var agentRunRepo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var agentStepRepo = scope.ServiceProvider.GetRequiredService<IAgentStepRepository>();
        var agentApprovalRepository = scope.ServiceProvider.GetRequiredService<IAgentApprovalRepository>();
        var agentDefRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var stepDecisionParser = scope.ServiceProvider.GetRequiredService<IStepDecisionParser>();
        var toolExecutor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var modelGateway = scope.ServiceProvider.GetRequiredService<IModelGateway>();
        var toolDefinitionRepository = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();
        var toolInvocationRepository = scope.ServiceProvider.GetRequiredService<IToolInvocationRepository>();
        var routeRuleRepository = scope.ServiceProvider.GetService<IRouteRuleRepository>();
        var runtimeContextComposer = scope.ServiceProvider.GetService<IRuntimeContextComposer>();
        var runtimeMemoryWriter = scope.ServiceProvider.GetService<IRuntimeMemoryWriter>();
        var agentOutputValidator = scope.ServiceProvider.GetService<IAgentOutputValidator>();

        var parentRun = await agentRunRepo.GetByRunIdAsync(runId, stoppingToken);
        if (parentRun is null || IsCancelled(parentRun))
        {
            return;
        }

        if (!string.Equals(parentRun.Status, "WaitingHandoff", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(waitToken)
            && !string.Equals(parentRun.CurrentWaitToken, waitToken, StringComparison.Ordinal))
        {
            await FailRun(agentRunRepo, agentStepRepo, parentRun, "Handoff wait token mismatch.", stoppingToken);
            return;
        }

        var handoffStep = await agentStepRepo.GetLastByRunIdAsync(runId, stoppingToken);
        if (handoffStep is null
            || !string.Equals(handoffStep.StepId, stepId, StringComparison.Ordinal)
            || !string.Equals(handoffStep.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(handoffStep.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            await FailRun(agentRunRepo, agentStepRepo, parentRun, "Handoff step is missing or no longer pending.", stoppingToken);
            return;
        }

        if (!HandoffPayloadSerializer.TryParse(handoffStep.DecisionPayload, out var handoffPayload)
            || !string.Equals(handoffPayload!.ChildRunId, childRunId, StringComparison.Ordinal))
        {
            await FailRun(agentRunRepo, agentStepRepo, parentRun, "Handoff payload is invalid.", stoppingToken);
            return;
        }

        var childRun = await agentRunRepo.GetByRunIdAsync(childRunId, stoppingToken);
        if (childRun is null)
        {
            await FailRun(agentRunRepo, agentStepRepo, parentRun, $"Child run '{childRunId}' was not found.", stoppingToken);
            return;
        }

        if (!IsTerminal(childRun.Status))
        {
            return;
        }

        var completedAt = DateTime.UtcNow;
        if (string.Equals(childRun.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            var completedPayload = HandoffPayloadSerializer.MarkCompleted(
                handoffPayload,
                childRun.Status,
                childRun.OutputPayload);
            handoffStep = handoffStep with
            {
                Status = "Completed",
                OutputPayload = childRun.OutputPayload,
                DecisionPayload = HandoffPayloadSerializer.Serialize(completedPayload),
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };
            await agentStepRepo.UpdateAsync(handoffStep, stoppingToken);

            await PublishEventAsync(
                runId,
                "step",
                "WaitingHandoff",
                new AgentRunEventData(
                    handoffStep.StepNo,
                    handoffStep.StepType,
                    "Completed",
                    childRun.OutputPayload,
                    DecisionPayload: handoffStep.DecisionPayload),
                stoppingToken);

            var resolvedDefinition = await GetResolvedDefinitionForRunAsync(agentDefRepo, agentRunRepo, agentStepRepo, parentRun, stoppingToken);
            if (resolvedDefinition is null)
            {
                await FailRun(agentRunRepo, agentStepRepo, parentRun, "Agent definition not found.", stoppingToken);
                return;
            }

            if (string.Equals(handoffPayload.Mode, "route_only", StringComparison.OrdinalIgnoreCase))
            {
                await CompleteRunAsync(
                    agentRunRepo,
                    agentStepRepo,
                    parentRun with
                    {
                        CurrentWaitToken = string.Empty
                    },
                    resolvedDefinition,
                    childRun.OutputPayload ?? string.Empty,
                    runtimeMemoryWriter,
                    agentOutputValidator,
                    stoppingToken);
                return;
            }

            var mergeStrategy = HandoffPayloadSerializer.NormalizeMergeStrategy(handoffPayload.Mode, handoffPayload.MergeStrategy);
            if (string.Equals(handoffPayload.Mode, "delegate_and_merge", StringComparison.OrdinalIgnoreCase)
                && string.Equals(mergeStrategy, "first_success", StringComparison.OrdinalIgnoreCase))
            {
                await CompleteRunAsync(
                    agentRunRepo,
                    agentStepRepo,
                    parentRun with
                    {
                        CurrentWaitToken = string.Empty
                    },
                    resolvedDefinition,
                    childRun.OutputPayload ?? string.Empty,
                    runtimeMemoryWriter,
                    agentOutputValidator,
                    stoppingToken);
                return;
            }

            parentRun = parentRun with
            {
                Status = "Running",
                CurrentWaitToken = string.Empty,
                StatusVersion = parentRun.StatusVersion + 1,
                LastModifyTime = completedAt
            };
            await agentRunRepo.UpdateAsync(parentRun, stoppingToken);

            var followUpInput = string.Equals(handoffPayload.Mode, "delegate_and_merge", StringComparison.OrdinalIgnoreCase)
                ? BuildHandoffMergeFollowUpInput(
                    parentRun.InputPayload ?? string.Empty,
                    handoffPayload.TargetAgent,
                    childRun.OutputPayload ?? string.Empty,
                    mergeStrategy)
                : BuildHandoffFollowUpInput(
                    parentRun.InputPayload ?? string.Empty,
                    handoffPayload.TargetAgent,
                    childRun.OutputPayload ?? string.Empty);
            var nextStepNo = parentRun.CurrentStepNo + 1;
            var startTurn = await CountCompletedModelTurnsAsync(agentStepRepo, runId, stoppingToken);
            var loopContext = new AgentLoopContext(parentRun, resolvedDefinition.Version, followUpInput, nextStepNo, startTurn);
            var loopResult = await AgentRunLoop.ExecuteAsync(
                loopContext,
                resolvedDefinition,
                modelGateway,
                stepDecisionParser,
                toolExecutor,
                agentStepRepo,
                toolDefinitionRepository,
                toolInvocationRepository,
                stoppingToken,
                evt => PublishEventAsync(evt.RunId, evt.EventType, parentRun.Status, evt.Data, stoppingToken),
                runtimeContextComposer,
                ResolveApprovalPolicyOptions(resolvedDefinition),
                routeRuleRepository);

            await ApplyLoopResult(
                agentRunRepo,
                agentStepRepo,
                agentApprovalRepository,
                parentRun,
                resolvedDefinition,
                loopResult,
                runtimeMemoryWriter,
                agentOutputValidator,
                stoppingToken);
            return;
        }

        var failureReason = string.IsNullOrWhiteSpace(childRun.InterruptReason)
            ? $"Child run '{childRun.RunId}' finished with status '{childRun.Status}'."
            : childRun.InterruptReason;
        var failedPayload = HandoffPayloadSerializer.MarkFailed(
            handoffPayload,
            childRun.Status,
            failureReason);
        handoffStep = handoffStep with
        {
            Status = "Failed",
            ErrorPayload = failureReason,
            DecisionPayload = HandoffPayloadSerializer.Serialize(failedPayload),
            EndedAt = completedAt,
            LastModifyTime = completedAt
        };
        await agentStepRepo.UpdateAsync(handoffStep, stoppingToken);

        await FailRun(agentRunRepo, agentStepRepo, parentRun, failureReason, stoppingToken);
        await PublishEventAsync(
            runId,
            "error",
            "Failed",
            new AgentRunEventData(
                handoffStep.StepNo,
                handoffStep.StepType,
                "Failed",
                Error: failureReason,
                DecisionPayload: handoffStep.DecisionPayload),
            stoppingToken);
    }

    private async Task ApplyLoopResult(
        IAgentRunRepository agentRunRepo,
        IAgentStepRepository agentStepRepo,
        IAgentApprovalRepository agentApprovalRepository,
        AgentRun agentRun,
        ResolvedAgentDefinition resolvedDefinition,
        AgentLoopResult loopResult,
        IRuntimeMemoryWriter? runtimeMemoryWriter,
        IAgentOutputValidator? agentOutputValidator,
        CancellationToken cancellationToken)
    {
        var latestRun = await agentRunRepo.GetByRunIdAsync(agentRun.RunId, cancellationToken);
        if (latestRun is null || IsCancelled(latestRun))
        {
            return;
        }

        agentRun = latestRun;
        var totalCostDelta = GetTotalCostDelta(loopResult);
        if (totalCostDelta > 0m)
        {
            agentRun = agentRun with
            {
                TotalCost = agentRun.TotalCost + totalCostDelta
            };
            await agentRunRepo.UpdateAsync(agentRun, cancellationToken);
        }

        switch (loopResult)
        {
            case AgentLoopCompleted completed:
                await CompleteRunAsync(
                    agentRunRepo,
                    agentStepRepo,
                    agentRun,
                    resolvedDefinition,
                    completed.Output,
                    runtimeMemoryWriter,
                    agentOutputValidator,
                    cancellationToken);
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
                await agentRunRepo.UpdateAsync(agentRun, cancellationToken);
                await PublishEventAsync(
                    agentRun.RunId,
                    "waiting",
                    "WaitingTool",
                    new AgentRunEventData(suspended.SuspendedAtStepNo, "tool_call", "Pending"),
                    cancellationToken);
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
                    ExpiresAt = ResolveApprovalExpiresAt(approvalAt),
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = approvalAt,
                    LastModifyTime = approvalAt
                };
                await agentApprovalRepository.AddAsync(approval, cancellationToken);
                _agentMetrics.RecordApprovalWaitStarted(agentRun.AgentCode, waitingApproval.StepType);
                var approvalStep = await agentStepRepo.GetByStepIdAsync(waitingApproval.StepId, cancellationToken);

                agentRun = agentRun with
                {
                    Status = "WaitingApproval",
                    CurrentWaitToken = waitingApproval.WaitToken,
                    CurrentStepNo = waitingApproval.SuspendedAtStepNo,
                    StatusVersion = agentRun.StatusVersion + 1,
                    LastModifyTime = approvalAt
                };
                await agentRunRepo.UpdateAsync(agentRun, cancellationToken);
                await PublishEventAsync(
                    agentRun.RunId,
                    "waiting_approval",
                    "WaitingApproval",
                    new AgentRunEventData(
                        waitingApproval.SuspendedAtStepNo,
                        waitingApproval.StepType,
                        "Pending",
                        waitingApproval.ToolName,
                        DecisionPayload: approvalStep?.DecisionPayload),
                    cancellationToken);
                break;

            case AgentLoopWaitingHuman waitingHuman:
                await StartWaitingHumanAsync(
                    agentRunRepo,
                    agentStepRepo,
                    agentRun,
                    waitingHuman,
                    cancellationToken);
                break;

            case AgentLoopWaitingHandoff waitingHandoff:
                await StartHandoffAsync(
                    agentRunRepo,
                    agentStepRepo,
                    resolvedDefinition,
                    agentRun,
                    waitingHandoff,
                    cancellationToken);
                break;

            case AgentLoopFailed failed:
                var failedAt = DateTime.UtcNow;
                agentRun = agentRun with
                {
                    Status = "Failed",
                    InterruptReason = failed.ErrorMessage[..Math.Min(failed.ErrorMessage.Length, 256)],
                    EndedAt = failedAt,
                    StatusVersion = agentRun.StatusVersion + 1,
                    LastModifyTime = failedAt
                };
                await agentRunRepo.UpdateAsync(agentRun, cancellationToken);
                RecordRunCompleted(agentRun);
                await PublishEventAsync(
                    agentRun.RunId,
                    "error",
                    "Failed",
                    new AgentRunEventData(failed.FailedAtStepNo, failed.StepType, "Failed", Error: failed.ErrorPayload),
                    cancellationToken);
                break;
        }
    }

    private static decimal GetTotalCostDelta(AgentLoopResult loopResult)
        => loopResult switch
        {
            AgentLoopCompleted completed => completed.TotalCostDelta,
            AgentLoopSuspended suspended => suspended.TotalCostDelta,
            AgentLoopWaitingApproval waitingApproval => waitingApproval.TotalCostDelta,
            AgentLoopWaitingHuman waitingHuman => waitingHuman.TotalCostDelta,
            AgentLoopWaitingHandoff waitingHandoff => waitingHandoff.TotalCostDelta,
            AgentLoopFailed failed => failed.TotalCostDelta,
            _ => 0m
        };

    private async Task CompleteRunAsync(
        IAgentRunRepository agentRunRepo,
        IAgentStepRepository agentStepRepo,
        AgentRun agentRun,
        ResolvedAgentDefinition resolvedDefinition,
        string output,
        IRuntimeMemoryWriter? runtimeMemoryWriter,
        IAgentOutputValidator? agentOutputValidator,
        CancellationToken cancellationToken)
    {
        if (agentOutputValidator is not null)
        {
            try
            {
                agentOutputValidator.Validate(
                    agentRun.AgentCode,
                    resolvedDefinition.Version.OutputSchema,
                    output);
            }
            catch (InvalidOperationException ex)
            {
                await FailRun(agentRunRepo, agentStepRepo, agentRun, ex.Message, cancellationToken);
                await PublishEventAsync(
                    agentRun.RunId,
                    "error",
                    "Failed",
                    new AgentRunEventData(
                        agentRun.CurrentStepNo + 1,
                        "respond",
                        "Failed",
                        Error: ex.Message),
                    cancellationToken);
                return;
            }
        }

        var completedAt = DateTime.UtcNow;
        agentRun = agentRun with
        {
            Status = "Completed",
            CurrentWaitToken = string.Empty,
            InterruptReason = string.Empty,
            OutputPayload = output,
            EndedAt = completedAt,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = completedAt
        };
        await agentRunRepo.UpdateAsync(agentRun, cancellationToken);
        RecordRunCompleted(agentRun);
        if (runtimeMemoryWriter is not null
            && AllowsSummaryMemoryWrite(resolvedDefinition))
        {
            await runtimeMemoryWriter.RecordRunCompletionSummaryAsync(agentRun, output, cancellationToken);
        }

        await PublishEventAsync(
            agentRun.RunId,
            "done",
            "Completed",
            new AgentRunEventData(0, "completed", "Completed", output),
            cancellationToken);
    }

    private async Task StartHandoffAsync(
        IAgentRunRepository agentRunRepo,
        IAgentStepRepository agentStepRepo,
        ResolvedAgentDefinition parentDefinition,
        AgentRun parentRun,
        AgentLoopWaitingHandoff waitingHandoff,
        CancellationToken cancellationToken)
    {
        using var activity = AgentTracing.Source.StartActivity(AgentTracing.HandoffActivityName, ActivityKind.Internal);
        activity?.SetTag("bestagent.parent_run_id", parentRun.RunId);
        activity?.SetTag("bestagent.parent_agent_code", parentRun.AgentCode);
        activity?.SetTag("bestagent.child_run_id", waitingHandoff.ChildRunId);
        activity?.SetTag("bestagent.target_agent", waitingHandoff.TargetAgent);
        activity?.SetTag("bestagent.handoff_mode", waitingHandoff.HandoffMode);

        using var scope = _scopeFactory.CreateScope();
        var agentDefinitionRepository = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var currentHandoffDepth = await GetHandoffDepthAsync(agentRunRepo, parentRun, cancellationToken);
        var maxHandoffDepth = ResolveMaxHandoffDepth(parentDefinition.Version.ExecutionPolicy);
        var nextHandoffDepth = currentHandoffDepth + 1;
        var parentHandoffPayload = await GetPendingHandoffPayloadAsync(agentStepRepo, parentRun.RunId, cancellationToken);
        if (nextHandoffDepth > maxHandoffDepth)
        {
            var error = $"Handoff depth {nextHandoffDepth} exceeds the configured maximum {maxHandoffDepth} for agent '{parentRun.AgentCode}'.";
            await FailRun(agentRunRepo, agentStepRepo, parentRun, error, cancellationToken);
            await PublishEventAsync(
                parentRun.RunId,
                "error",
                "Failed",
                new AgentRunEventData(
                    waitingHandoff.SuspendedAtStepNo,
                    "handoff",
                    "Failed",
                    Error: error,
                    DecisionPayload: parentHandoffPayload is null
                        ? null
                        : HandoffPayloadSerializer.Serialize(parentHandoffPayload)),
                cancellationToken);
            activity?.SetTag("bestagent.status", "failed");
            activity?.SetStatus(ActivityStatusCode.Error, error);
            return;
        }

        var childDefinition = await agentDefinitionRepository.GetEnabledByCodeAsync(waitingHandoff.TargetAgent, cancellationToken);
        if (childDefinition is null)
        {
            await FailRun(agentRunRepo, agentStepRepo, parentRun, $"Agent definition '{waitingHandoff.TargetAgent}' not found.", cancellationToken);
            await PublishEventAsync(
                parentRun.RunId,
                "error",
                "Failed",
                new AgentRunEventData(
                    waitingHandoff.SuspendedAtStepNo,
                    "handoff",
                    "Failed",
                    Error: $"Agent definition '{waitingHandoff.TargetAgent}' not found.",
                    DecisionPayload: parentHandoffPayload is null
                        ? null
                        : HandoffPayloadSerializer.Serialize(parentHandoffPayload)),
                cancellationToken);
            activity?.SetTag("bestagent.status", "failed");
            activity?.SetStatus(ActivityStatusCode.Error, $"Agent definition '{waitingHandoff.TargetAgent}' not found.");
            return;
        }

        var now = DateTime.UtcNow;
        var childInput = BuildChildRunInput(parentRun.InputPayload, waitingHandoff.HandoffInput, parentHandoffPayload);
        var childRun = CreateAgentRunCommandHandler.BuildAgentRun(
            waitingHandoff.ChildRunId,
            waitingHandoff.TargetAgent,
            childDefinition.Version.Id,
            childInput,
            waitingHandoff.ChildRunId,
            parentRun.TenantId,
            parentRun.UserId,
            parentRun.SessionId,
            childDefinition.Version.MaxTurns,
            childDefinition.Version.MaxCost,
            now,
            parentRun.RunId,
            string.IsNullOrWhiteSpace(parentRun.RootRunId) ? parentRun.RunId : parentRun.RootRunId,
            parentRun.RunId,
            parentRun.AgentCode);

        await agentRunRepo.AddAsync(childRun, cancellationToken);
        _agentMetrics.RecordRunCreated(childRun.AgentCode, isChildRun: true);
        await agentStepRepo.AddAsync(
            AgentRunLoop.CreateStep(
                childRun.RunId,
                1,
                "created",
                "Completed",
                childInput,
                null,
                null,
                now,
                now),
            cancellationToken);
        await agentStepRepo.AddAsync(
            AgentRunLoop.CreateStep(
                childRun.RunId,
                2,
                "running",
                "Completed",
                childInput,
                null,
                null,
                now,
                now),
            cancellationToken);

        parentRun = parentRun with
        {
            Status = "WaitingHandoff",
            CurrentWaitToken = waitingHandoff.WaitToken,
            CurrentStepNo = waitingHandoff.SuspendedAtStepNo,
            StatusVersion = parentRun.StatusVersion + 1,
            LastModifyTime = now
        };
        await agentRunRepo.UpdateAsync(parentRun, cancellationToken);

        await PublishEventAsync(
            parentRun.RunId,
            "waiting_handoff",
            "WaitingHandoff",
            new AgentRunEventData(
                waitingHandoff.SuspendedAtStepNo,
                "handoff",
                "Pending",
                waitingHandoff.TargetAgent,
                DecisionPayload: parentHandoffPayload is null
                    ? null
                    : HandoffPayloadSerializer.Serialize(parentHandoffPayload)),
            cancellationToken);

        await _channel.EnqueueAsync(new CreateAgentRunMessage(childRun.RunId), cancellationToken);
        activity?.SetTag("bestagent.status", "waiting_handoff");
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private static async Task<int> GetHandoffDepthAsync(
        IAgentRunRepository agentRunRepository,
        AgentRun agentRun,
        CancellationToken cancellationToken)
    {
        var depth = 0;
        var currentParentRunId = agentRun.ParentRunId;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrWhiteSpace(currentParentRunId))
        {
            if (!visited.Add(currentParentRunId))
            {
                throw new InvalidOperationException($"Detected a cycle in parent run chain at '{currentParentRunId}'.");
            }

            depth++;
            var parentRun = await agentRunRepository.GetByRunIdAsync(currentParentRunId, cancellationToken);
            if (parentRun is null)
            {
                break;
            }

            currentParentRunId = parentRun.ParentRunId;
        }

        return depth;
    }

    private static async Task<HandoffPayload?> GetPendingHandoffPayloadAsync(
        IAgentStepRepository agentStepRepository,
        string runId,
        CancellationToken cancellationToken)
    {
        var step = await agentStepRepository.GetLastByRunIdAsync(runId, cancellationToken);
        if (step is null
            || !string.Equals(step.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
            || !HandoffPayloadSerializer.TryParse(step.DecisionPayload, out var payload))
        {
            return null;
        }

        return payload;
    }

    private static string BuildChildRunInput(
        string? parentInput,
        string? handoffInput,
        HandoffPayload? parentHandoffPayload)
    {
        var effectiveInput = string.IsNullOrWhiteSpace(handoffInput)
            ? parentInput ?? string.Empty
            : handoffInput.Trim();
        if (parentHandoffPayload is null)
        {
            return effectiveInput;
        }

        var contextMode = ResolveContextMode(parentHandoffPayload.ContextOverrides)
            ?? ResolveContextMode(parentHandoffPayload.ContextScope);
        if (!string.Equals(contextMode, "summary_only", StringComparison.OrdinalIgnoreCase))
        {
            return effectiveInput;
        }

        return
            $"""
            Delegated task summary:
            {effectiveInput}

            Parent user request summary:
            {NormalizeMultilineText(parentInput)}

            Use only the summarized delegated task and parent request context above for this child run.
            """;
    }

    private static string? ResolveContextMode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ReadString(document.RootElement, "mode")
                ?? ReadString(document.RootElement, "contextMode");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeMultilineText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        return value.Trim();
    }

    private static int ResolveMaxHandoffDepth(string? executionPolicy)
    {
        if (string.IsNullOrWhiteSpace(executionPolicy))
        {
            return DefaultMaxHandoffDepth;
        }

        try
        {
            using var document = JsonDocument.Parse(executionPolicy);
            var root = document.RootElement;
            var configuredDepth = ReadInt32(root, "maxHandoffDepth")
                ?? ReadInt32(root, "handoffMaxDepth")
                ?? ReadInt32(root, "maxDelegationDepth");

            return configuredDepth is null
                ? DefaultMaxHandoffDepth
                : Math.Max(0, configuredDepth.Value);
        }
        catch (JsonException)
        {
            return DefaultMaxHandoffDepth;
        }
    }

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private async Task FailRun(
        IAgentRunRepository runRepo, IAgentStepRepository stepRepo,
        AgentRun agentRun, string error, CancellationToken ct, string? stepErrorPayload = null)
    {
        if (IsCancelled(agentRun))
        {
            return;
        }

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
        RecordRunCompleted(agentRun);
        await stepRepo.AddAsync(AgentRunLoop.CreateStep(
            agentRun.RunId, agentRun.CurrentStepNo + 1, "failed", "Failed",
            agentRun.InputPayload, null, stepErrorPayload ?? truncatedError, failedAt, failedAt), ct);
    }

    private static bool IsCancelled(AgentRun agentRun)
    {
        return string.Equals(agentRun.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminal(string? status)
    {
        return string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "TimedOut", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ResumeParentRunIfNeededAsync(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        AgentRun agentRun,
        CancellationToken cancellationToken)
    {
        if (!IsTerminal(agentRun.Status)
            || string.IsNullOrWhiteSpace(agentRun.ParentRunId))
        {
            return;
        }

        var parentRun = await agentRunRepository.GetByRunIdAsync(agentRun.ParentRunId, cancellationToken);
        if (parentRun is null
            || !string.Equals(parentRun.Status, "WaitingHandoff", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var parentStep = await agentStepRepository.GetLastByRunIdAsync(parentRun.RunId, cancellationToken);
        if (parentStep is null
            || !string.Equals(parentStep.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parentStep.Status, "Pending", StringComparison.OrdinalIgnoreCase)
            || !HandoffPayloadSerializer.TryParse(parentStep.DecisionPayload, out var handoffPayload)
            || !string.Equals(handoffPayload!.ChildRunId, agentRun.RunId, StringComparison.Ordinal))
        {
            return;
        }

        await _channel.EnqueueAsync(
            new ResumeParentHandoffMessage(
                parentRun.RunId,
                parentStep.StepId,
                parentRun.CurrentWaitToken,
                agentRun.RunId),
            cancellationToken);
    }

    private static string BuildHandoffFollowUpInput(
        string originalInput,
        string targetAgent,
        string childOutput)
    {
        return
            $"""
            Original user input:
            {originalInput}

            Handoff target:
            {targetAgent}

            Handoff result:
            {childOutput}

            Produce the final user-facing answer now.
            """;
    }

    private static string BuildHandoffMergeFollowUpInput(
        string originalInput,
        string targetAgent,
        string childOutput,
        string? mergeStrategy)
    {
        var normalizedMergeStrategy = HandoffPayloadSerializer.NormalizeMergeStrategy("delegate_and_merge", mergeStrategy);
        if (string.Equals(normalizedMergeStrategy, "all_results", StringComparison.OrdinalIgnoreCase))
        {
            return
                $"""
                Original user input:
                {originalInput}

                Child results to consolidate:
                [1] Agent: {targetAgent}
                Status: completed
                Output:
                {childOutput}

                Consolidate all child results into the final user-facing answer. Preserve material facts from the child output, resolve obvious duplication, and respond directly to the user.
                """;
        }

        return
            $"""
            Original user input:
            {originalInput}

            Handoff target:
            {targetAgent}

            Handoff result to merge:
            {childOutput}

            Merge the child result into a final user-facing answer. Preserve the useful child output, add any necessary synthesis, and respond to the user directly.
            """;
    }

    private static ToolInvocation CompleteToolInvocation(
        ToolInvocation invocation,
        string output,
        DateTime completedAt)
    {
        var durationMs = invocation.StartedAt is null
            ? 0
            : Math.Max(0, (long)(completedAt - invocation.StartedAt.Value).TotalMilliseconds);

        return invocation with
        {
            Status = "Completed",
            OutputPayload = output,
            EndedAt = completedAt,
            DurationMs = durationMs,
            LastModifyTime = completedAt
        };
    }

    private static ToolInvocation FailToolInvocation(
        ToolInvocation invocation,
        string error,
        DateTime completedAt)
    {
        var durationMs = invocation.StartedAt is null
            ? 0
            : Math.Max(0, (long)(completedAt - invocation.StartedAt.Value).TotalMilliseconds);

        return invocation with
        {
            Status = "Failed",
            ErrorPayload = error,
            EndedAt = completedAt,
            DurationMs = durationMs,
            LastModifyTime = completedAt
        };
    }

    private static bool TryParseCompletedToolResultEnvelope(
        string toolName,
        string toolResult,
        out ToolExecutionResult parsedResult)
    {
        parsedResult = default!;

        try
        {
            using var document = JsonDocument.Parse(toolResult);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("status", out var statusElement)
                || statusElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var status = statusElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            if (IsCompletedToolResultStatus(status))
            {
                var output = document.RootElement.TryGetProperty("data", out var dataElement)
                    ? SerializeToolResultPayload(dataElement)
                    : document.RootElement.TryGetProperty("output", out var outputElement)
                        ? SerializeToolResultPayload(outputElement)
                        : string.Empty;
                parsedResult = ToolExecutionResult.Completed(toolName, output, ReadToolResultMeta(document.RootElement));
                return true;
            }

            if (IsFailedToolResultStatus(status))
            {
                var error = document.RootElement.TryGetProperty("error", out var errorElement)
                    ? SerializeToolResultPayload(errorElement)
                    : document.RootElement.GetRawText();
                parsedResult = ToolExecutionResult.Failed(toolName, error, ReadToolResultMeta(document.RootElement));
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsCompletedToolResultStatus(string status)
    {
        return status.Equals("succeeded", StringComparison.OrdinalIgnoreCase)
            || status.Equals("success", StringComparison.OrdinalIgnoreCase)
            || status.Equals("completed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailedToolResultStatus(string status)
    {
        return status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("error", StringComparison.OrdinalIgnoreCase);
    }

    private static string SerializeToolResultPayload(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText();
    }

    private static string? ReadToolResultMeta(JsonElement root)
    {
        return root.TryGetProperty("meta", out var metaElement)
            ? metaElement.GetRawText()
            : null;
    }

    private void RecordRunCompleted(AgentRun agentRun)
    {
        _agentMetrics.RecordRunCompleted(agentRun.AgentCode, agentRun.Status, agentRun.TotalCost);
    }

    private void RecordApprovalWaitCompleted(
        string agentCode,
        string stepType,
        string outcome,
        DateTime startedAt,
        DateTime completedAt)
    {
        var duration = completedAt >= startedAt
            ? completedAt - startedAt
            : TimeSpan.Zero;
        _agentMetrics.RecordApprovalWaitCompleted(agentCode, stepType, outcome, duration);
    }

    private async Task<string> FailApprovedToolStepAsync(
        IAgentRunRepository agentRunRepo,
        IAgentStepRepository agentStepRepo,
        IToolInvocationRepository toolInvocationRepository,
        AgentRun agentRun,
        AgentStep pendingStep,
        ApprovalPayload approvedPayload,
        string toolName,
        string? toolInput,
        string errorPayload,
        string errorMessage,
        DateTime startedAt,
        DateTime failedAt,
        CancellationToken cancellationToken)
    {
        pendingStep = pendingStep with
        {
            Status = "Failed",
            ErrorPayload = errorPayload,
            DecisionPayload = ApprovalPayloadSerializer.Serialize(approvedPayload),
            StartedAt = startedAt,
            EndedAt = failedAt,
            DurationMs = Math.Max(0, (long)(failedAt - startedAt).TotalMilliseconds),
            LastModifyTime = failedAt
        };
        await agentStepRepo.UpdateAsync(pendingStep, cancellationToken);

        var failedInvocationId = Guid.NewGuid().ToString("N");
        await toolInvocationRepository.AddAsync(
            AgentRunLoop.CreateToolInvocation(
                failedInvocationId,
                agentRun.RunId,
                pendingStep.StepId,
                toolName,
                "sync",
                "Failed",
                toolInput,
                null,
                errorPayload,
                failedInvocationId,
                string.Empty,
                startedAt,
                failedAt,
                failedAt),
            cancellationToken);

        agentRun = agentRun with
        {
            Status = "Failed",
            CurrentWaitToken = string.Empty,
            InterruptReason = errorMessage[..Math.Min(errorMessage.Length, 256)],
            EndedAt = failedAt,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = failedAt
        };
        await agentRunRepo.UpdateAsync(agentRun, cancellationToken);
        RecordRunCompleted(agentRun);
        return failedInvocationId;
    }

    private static string BuildToolFollowUpInput(
        string originalInput,
        string toolResult,
        string? toolName = null,
        bool isHumanReplacement = false,
        string? originalToolResult = null,
        string? replacementReason = null)
    {
        var toolCalledSection = string.IsNullOrWhiteSpace(toolName)
            ? string.Empty
            : $"""

            Tool called:
            {toolName}
            """;
        var originalResultSection = !isHumanReplacement || string.IsNullOrWhiteSpace(originalToolResult)
            ? string.Empty
            : $"""

            Original tool result before human override:
            {originalToolResult}
            """;
        var replacementSection = !isHumanReplacement
            ? string.Empty
            : $"""

            Tool result was provided by a human operator instead of the original tool callback.
            Replacement reason:
            {replacementReason ?? "Human operator supplied the replacement result."}
            """;
        return
            $"""
            Original user input:
            {originalInput}
            {toolCalledSection}
            {originalResultSection}

            Tool result:
            {toolResult}
            {replacementSection}

            Produce the final user-facing answer now.
            """;
    }

    private static string ExtractToolNameFromStep(AgentStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.DecisionPayload))
        {
            if (TryReadToolNameProperty(step.DecisionPayload, out var toolName))
            {
                return toolName;
            }

            try
            {
                var payload = ApprovalPayloadSerializer.Parse(step.DecisionPayload);
                if (!string.IsNullOrWhiteSpace(payload.ToolName))
                {
                    return payload.ToolName;
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        return "tool_call";
    }

    private static bool TryReadToolNameProperty(string json, out string toolName)
    {
        toolName = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("toolName", out var toolNameProperty)
                || toolNameProperty.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var value = toolNameProperty.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            toolName = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static async Task<int> CountCompletedModelTurnsAsync(
        IAgentStepRepository agentStepRepository,
        string runId,
        CancellationToken cancellationToken)
    {
        var stepsTask = agentStepRepository.ListByRunIdAsync(runId, cancellationToken);
        if (stepsTask is null)
        {
            return 0;
        }

        var steps = await stepsTask;

        return steps.Count(step =>
            string.Equals(step.StepType, "model_call", StringComparison.OrdinalIgnoreCase)
            && string.Equals(step.Status, "Completed", StringComparison.OrdinalIgnoreCase));
    }

    private static bool AllowsToolResultMemoryWrite(
        ResolvedAgentDefinition resolvedDefinition,
        string? toolName)
    {
        return MemoryPolicy.Parse(resolvedDefinition.Version.MemoryPolicy)
            .AllowsToolResultWrite(toolName);
    }

    private static bool AllowsSummaryMemoryWrite(ResolvedAgentDefinition resolvedDefinition)
    {
        return MemoryPolicy.Parse(resolvedDefinition.Version.MemoryPolicy)
            .AllowsSummaryMemoryWrite();
    }

    private async Task<ResolvedAgentDefinition?> GetResolvedDefinitionForRunAsync(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        AgentRun agentRun,
        CancellationToken cancellationToken)
    {
        ResolvedAgentDefinition? resolvedDefinition;
        if (!string.IsNullOrWhiteSpace(agentRun.AgentDefinitionVersionId))
        {
            var boundDefinition = await agentDefinitionRepository.GetByVersionIdAsync(
                agentRun.AgentDefinitionVersionId,
                cancellationToken);
            if (boundDefinition is not null)
            {
                resolvedDefinition = boundDefinition;
                return await ApplyHandoffBoundaryRestrictionsIfNeeded(
                    agentDefinitionRepository,
                    agentRunRepository,
                    agentStepRepository,
                    agentRun,
                    resolvedDefinition,
                    cancellationToken);
            }
        }

        resolvedDefinition = await agentDefinitionRepository.GetEnabledByCodeAsync(agentRun.AgentCode, cancellationToken);
        return await ApplyHandoffBoundaryRestrictionsIfNeeded(
            agentDefinitionRepository,
            agentRunRepository,
            agentStepRepository,
            agentRun,
            resolvedDefinition,
            cancellationToken);
    }

    private async Task<ResolvedAgentDefinition?> ApplyHandoffBoundaryRestrictionsIfNeeded(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        AgentRun agentRun,
        ResolvedAgentDefinition? resolvedDefinition,
        CancellationToken cancellationToken)
    {
        if (resolvedDefinition is null
            || string.IsNullOrWhiteSpace(agentRun.ParentRunId))
        {
            return resolvedDefinition;
        }

        var parentStep = await agentStepRepository.GetLastByRunIdAsync(agentRun.ParentRunId, cancellationToken);
        if (parentStep is null
            || !string.Equals(parentStep.StepType, "handoff", StringComparison.OrdinalIgnoreCase)
            || !HandoffPayloadSerializer.TryParse(parentStep.DecisionPayload, out var handoffPayload)
            || !string.Equals(handoffPayload!.ChildRunId, agentRun.RunId, StringComparison.Ordinal))
        {
            return resolvedDefinition;
        }

        var parentRun = await agentRunRepository.GetByRunIdAsync(agentRun.ParentRunId, cancellationToken);
        if (parentRun is null)
        {
            return resolvedDefinition;
        }

        var parentResolvedDefinition = await GetResolvedDefinitionForRunAsync(
            agentDefinitionRepository,
            agentRunRepository,
            agentStepRepository,
            parentRun,
            cancellationToken);
        if (parentResolvedDefinition is null)
        {
            return resolvedDefinition;
        }

        var restrictedTools = ResolveRestrictedAllowedTools(handoffPayload, resolvedDefinition.Version.AllowedTools);
        var restrictedKnowledgeSources = ResolveRestrictedKnowledgeSources(handoffPayload, resolvedDefinition.Version.KnowledgeSources);
        var restrictedHandoffs = ResolveRestrictedAllowedHandoffs(
            resolvedDefinition.Version.AllowedHandoffs,
            parentResolvedDefinition.Version.AllowedHandoffs);
        var restrictedApprovalPolicy = ResolveRestrictedApprovalPolicy(
            resolvedDefinition.Version.ApprovalPolicy,
            parentResolvedDefinition.Version.ApprovalPolicy);
        var restrictedMemoryPolicy = ResolveRestrictedMemoryPolicy(
            handoffPayload,
            resolvedDefinition.Version.MemoryPolicy);
        if (restrictedTools is null
            && restrictedKnowledgeSources is null
            && restrictedHandoffs is null
            && restrictedApprovalPolicy is null
            && restrictedMemoryPolicy is null)
        {
            return resolvedDefinition;
        }

        return resolvedDefinition with
        {
            Version = resolvedDefinition.Version with
            {
                AllowedTools = restrictedTools ?? resolvedDefinition.Version.AllowedTools,
                KnowledgeSources = restrictedKnowledgeSources ?? resolvedDefinition.Version.KnowledgeSources,
                AllowedHandoffs = restrictedHandoffs ?? resolvedDefinition.Version.AllowedHandoffs,
                ApprovalPolicy = restrictedApprovalPolicy ?? resolvedDefinition.Version.ApprovalPolicy,
                MemoryPolicy = restrictedMemoryPolicy ?? resolvedDefinition.Version.MemoryPolicy
            }
        };
    }

    private static string? ResolveRestrictedAllowedTools(HandoffPayload handoffPayload, string? currentAllowedTools)
    {
        var allowedOverride = ReadStringArrayOverride(handoffPayload.ToolOverrides, "allowed")
            ?? ReadStringArrayOverride(handoffPayload.ToolScope, "allowed");
        if (allowedOverride is null)
        {
            return null;
        }

        var currentAllowed = ParseStringList(currentAllowedTools);
        var restricted = currentAllowed
            .Where(tool => allowedOverride.Contains(tool, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(restricted);
    }

    private static string? ResolveRestrictedKnowledgeSources(HandoffPayload handoffPayload, string? currentKnowledgeSources)
    {
        var allowedOverride = ReadStringArrayOverride(handoffPayload.KnowledgeOverrides, "allowed", "sources")
            ?? ReadStringArrayOverride(handoffPayload.KnowledgeScope, "allowed", "sources");
        if (allowedOverride is null)
        {
            return null;
        }

        var currentAllowed = ParseStringList(currentKnowledgeSources);
        var restricted = currentAllowed
            .Where(source => allowedOverride.Contains(source, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(restricted);
    }

    private static string? ResolveRestrictedAllowedHandoffs(string? currentAllowedHandoffs, string? parentAllowedHandoffs)
    {
        var currentAllowed = ParseStringList(currentAllowedHandoffs);
        var parentAllowed = ParseStringList(parentAllowedHandoffs);
        var restricted = currentAllowed
            .Where(agentCode => parentAllowed.Contains(agentCode, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(restricted);
    }

    private static string? ResolveRestrictedApprovalPolicy(string? currentApprovalPolicy, string? parentApprovalPolicy)
    {
        var currentPolicy = ApprovalPolicyParser.ParseOptional(currentApprovalPolicy);
        var parentPolicy = ApprovalPolicyParser.ParseOptional(parentApprovalPolicy);
        if (currentPolicy is null && parentPolicy is null)
        {
            return null;
        }

        var merged = ApprovalPolicyInheritance.MergeStricter(parentPolicy, currentPolicy);
        return JsonSerializer.Serialize(merged);
    }

    private static string? ResolveRestrictedMemoryPolicy(HandoffPayload handoffPayload, string? currentMemoryPolicy)
    {
        var contextMode = ResolveContextMode(handoffPayload.ContextOverrides)
            ?? ResolveContextMode(handoffPayload.ContextScope);
        var memoryMode = ResolveContextMode(handoffPayload.MemoryOverrides)
            ?? ResolveContextMode(handoffPayload.MemoryScope);

        var restrictReadContext = string.Equals(contextMode, "summary_only", StringComparison.OrdinalIgnoreCase);
        var disableMemoryContext = string.Equals(memoryMode, "disabled", StringComparison.OrdinalIgnoreCase);
        var restrictMemoryWrites = restrictReadContext
            || disableMemoryContext
            || string.Equals(memoryMode, "read_only", StringComparison.OrdinalIgnoreCase);
        if (!restrictReadContext && !disableMemoryContext && !restrictMemoryWrites)
        {
            return null;
        }

        var policy = MemoryPolicy.Parse(currentMemoryPolicy);
        return JsonSerializer.Serialize(new
        {
            toolResultMemoryEnabled = restrictMemoryWrites ? false : policy.ToolResultMemoryEnabled,
            toolResultMemoryAllowedTools = policy.AllowedToolNames.Count == 0 ? null : policy.AllowedToolNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            userMemoryWriteEnabled = restrictMemoryWrites ? false : policy.UserMemoryWriteEnabled,
            summaryMemoryWriteEnabled = restrictMemoryWrites ? false : policy.SummaryMemoryWriteEnabled,
            includeSummary = restrictReadContext || disableMemoryContext ? false : policy.IncludeSummary,
            includeKnowledge = restrictReadContext || disableMemoryContext ? false : policy.IncludeKnowledge,
            maxKnowledgeChunks = restrictReadContext || disableMemoryContext ? 0 : policy.MaxKnowledgeChunks,
            knowledgeCandidateCount = restrictReadContext || disableMemoryContext ? 0 : policy.KnowledgeCandidateCount,
            includeSessionMemory = restrictReadContext || disableMemoryContext ? false : policy.IncludeSessionMemory,
            maxSessionMemories = restrictReadContext || disableMemoryContext ? 0 : policy.MaxSessionMemories,
            includeUserMemory = restrictReadContext || disableMemoryContext ? false : policy.IncludeUserMemory,
            maxUserMemories = restrictReadContext || disableMemoryContext ? 0 : policy.MaxUserMemories
        });
    }

    private static string[]? ReadStringArrayOverride(string? json, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!document.RootElement.TryGetProperty(propertyName, out var valuesElement)
                    || valuesElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                return valuesElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string[] ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private ApprovalPolicyOptions ResolveApprovalPolicyOptions(ResolvedAgentDefinition resolvedDefinition)
    {
        var versionPolicy = ApprovalPolicyParser.ParseOptional(resolvedDefinition.Version.ApprovalPolicy);
        return ApprovalPolicyParser.Merge(_approvalPolicyOptions, versionPolicy);
    }

    private static async Task<ToolOutputValidationFailure?> ValidateToolOutputAsync(
        IToolDefinitionRepository toolDefinitionRepository,
        IToolOutputValidator? toolOutputValidator,
        string? toolName,
        string output,
        CancellationToken cancellationToken)
    {
        if (toolOutputValidator is null
            || string.IsNullOrWhiteSpace(toolName)
            || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var definition = await toolDefinitionRepository.GetByToolNameAsync(toolName, cancellationToken);
        if (definition is null)
        {
            return null;
        }

        try
        {
            toolOutputValidator.Validate(definition, output);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return new ToolOutputValidationFailure(ex.Message, definition.CompensationPolicy);
        }
    }

    private static async Task<string?> GetCompensationPolicyAsync(
        IToolDefinitionRepository toolDefinitionRepository,
        string? toolName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        var definition = await toolDefinitionRepository.GetByToolNameAsync(toolName, cancellationToken);
        return definition?.CompensationPolicy;
    }

    private async Task StartWaitingHumanAsync(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        AgentRun agentRun,
        AgentLoopWaitingHuman waitingHuman,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var waitToken = string.IsNullOrWhiteSpace(waitingHuman.WaitToken)
            ? Guid.NewGuid().ToString("N")
            : waitingHuman.WaitToken;
        var humanPayload = HumanApprovalPayloadSerializer.CreatePending(
            waitingHuman.Comment,
            sourceType: waitingHuman.SourceType,
            sourceStepId: waitingHuman.SourceStepId,
            sourceInvocationId: waitingHuman.SourceInvocationId,
            sourceToolName: waitingHuman.ToolName,
            sourceToolInput: waitingHuman.ToolInput,
            sourceToolOutput: waitingHuman.SourceToolOutput,
            sourceToolStatus: waitingHuman.SourceToolStatus,
            continueAsToolResult: waitingHuman.ContinueAsToolResult);
        var humanStep = AgentRunLoop.CreateStep(
            agentRun.RunId,
            waitingHuman.SuspendedAtStepNo,
            "human_wait",
            "Pending",
            waitingHuman.Comment,
            null,
            null,
            now,
            now) with
        {
            DecisionPayload = HumanApprovalPayloadSerializer.Serialize(humanPayload)
        };
        await agentStepRepository.AddAsync(humanStep, cancellationToken);

        var waitingHumanRun = agentRun with
        {
            Status = "WaitingHuman",
            CurrentWaitToken = waitToken,
            CurrentStepNo = waitingHuman.SuspendedAtStepNo,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = now
        };
        await agentRunRepository.UpdateAsync(waitingHumanRun, cancellationToken);

        await PublishEventAsync(
            agentRun.RunId,
            "waiting_human",
            "WaitingHuman",
            new AgentRunEventData(
                humanStep.StepNo,
                humanStep.StepType,
                "Pending",
                waitingHuman.Comment,
                DecisionPayload: humanStep.DecisionPayload),
            cancellationToken);
    }

    private Task StartManualCompensationAsync(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        AgentRun agentRun,
        int humanStepNo,
        string sourceStepId,
        string sourceInvocationId,
        string toolName,
        string? toolInput,
        string? sourceToolOutput,
        string comment,
        CancellationToken cancellationToken)
    {
        return StartWaitingHumanAsync(
            agentRunRepository,
            agentStepRepository,
            agentRun,
            new AgentLoopWaitingHuman(
                Guid.NewGuid().ToString("N"),
                humanStepNo,
                sourceStepId,
                sourceInvocationId,
                toolName,
                toolInput,
                sourceToolOutput,
                comment,
                "tool_failure",
                "Failed",
                true),
            cancellationToken);
    }

    private static string BuildManualCompensationComment(string toolName)
    {
        return $"Tool '{toolName}' failed and requires manual compensation.";
    }

    private DateTime? ResolveApprovalExpiresAt(DateTime approvalAt)
    {
        return _approvalTimeoutOptions.TimeoutMinutes > 0
            ? approvalAt.AddMinutes(_approvalTimeoutOptions.TimeoutMinutes)
            : null;
    }

    private async Task PublishEventAsync(
        string runId,
        string eventType,
        string runStatus,
        AgentRunEventData data,
        CancellationToken cancellationToken)
    {
        var occurredAt = DateTime.UtcNow;
        var storedEvent = await StoreOutboxEventAsync(runId, eventType, runStatus, data, occurredAt, cancellationToken);
        var evt = new AgentRunEvent(
            runId,
            eventType,
            data,
            storedEvent?.EventId,
            storedEvent?.SeqNo,
            runStatus,
            storedEvent?.OccurredAt ?? occurredAt);
        _eventBus.Publish(evt);
        if (!string.IsNullOrWhiteSpace(storedEvent?.EventId))
        {
            await MarkOutboxEventPublishedAsync(storedEvent.EventId, cancellationToken);
        }
    }

    private async Task<StoredOutboxEvent?> StoreOutboxEventAsync(
        string runId,
        string eventType,
        string runStatus,
        AgentRunEventData data,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var outboxRepository = scope.ServiceProvider.GetService<IRunOutboxEventRepository>();
            if (outboxRepository is null)
            {
                return null;
            }

            var nextSeqNo = await outboxRepository.GetNextSeqNoAsync(runId, cancellationToken);
            var eventId = Guid.NewGuid().ToString("N");
            await outboxRepository.AddAsync(
                new RunOutboxEvent
                {
                    EventId = eventId,
                    RunId = runId,
                    SeqNo = nextSeqNo,
                    EventType = eventType,
                    RunStatus = runStatus,
                    Payload = JsonSerializer.Serialize(data),
                    PublishStatus = "pending",
                    OccurredAt = occurredAt,
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = occurredAt,
                    LastModifyTime = occurredAt
                },
                cancellationToken);

            return new StoredOutboxEvent(eventId, nextSeqNo, occurredAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store outbox event {EventType} for run {RunId}", eventType, runId);
            return null;
        }
    }

    private async Task MarkOutboxEventPublishedAsync(string eventId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var outboxRepository = scope.ServiceProvider.GetService<IRunOutboxEventRepository>();
            if (outboxRepository is null)
            {
                return;
            }

            await outboxRepository.MarkPublishedAsync(eventId, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark outbox event {EventId} as published", eventId);
        }
    }

    private sealed record ToolOutputValidationFailure(
        string Message,
        string? CompensationPolicy);

    private sealed record StoredOutboxEvent(
        string EventId,
        long SeqNo,
        DateTime OccurredAt);
}
