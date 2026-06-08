using BestAgent.Application.AgentDefinitions;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BestAgent.Application.AgentRuns.Runtime;

public static class AgentRunLoop
{
    private const string RuntimeInstruction =
        """
        Return JSON only.
        Use {"action":"respond","response":"..."} for a final answer.
        Use {"action":"retrieve","query":"..."} to retrieve relevant knowledge before answering.
        Use {"action":"tool_call","toolName":"...","toolInput":"..."} to call one tool.
        Use {"action":"handoff","targetAgent":"...","input":"...","mode":"route_only"} to route directly to another allowed agent.
        Use {"action":"handoff","targetAgent":"...","input":"...","mode":"delegate_and_wait"} to delegate to another allowed agent and then continue.
        Use {"action":"handoff","targetAgent":"...","input":"...","mode":"delegate_and_merge"} to delegate and then merge the child result into the final answer.
        Optional handoff fields: "reason", "confidence", "context_overrides", "memory_overrides", "tool_overrides", "knowledge_overrides", "approval_required", "merge_strategy".
        Supported merge_strategy values for delegate_and_merge: "supervisor_summary", "first_success", "all_results".
        Use {"action":"request_approval","requestedAction":"...","requestPayload":"...","sideEffectLevel":"internal_write","comment":"..."} when a human must explicitly approve the next action before you continue.
        Use {"action":"request_human","comment":"..."} when a human operator must review or provide the answer.
        Use {"action":"fail","errorCode":"...","message":"..."} when the run cannot continue automatically.
        """;

    public static async Task<AgentLoopResult> ExecuteAsync(
        AgentLoopContext context,
        ResolvedAgentDefinition resolvedDefinition,
        IModelGateway modelGateway,
        IStepDecisionParser stepDecisionParser,
        IToolExecutor toolExecutor,
        IAgentStepRepository agentStepRepository,
        IToolDefinitionRepository toolDefinitionRepository,
        IToolInvocationRepository toolInvocationRepository,
        CancellationToken cancellationToken,
        Func<AgentRunEvent, Task>? onEvent = null,
        IRuntimeContextComposer? runtimeContextComposer = null,
        ApprovalPolicyOptions? approvalPolicyOptions = null,
        IRouteRuleRepository? routeRuleRepository = null)
    {
        var run = context.Run;
        var version = context.Version;
        var currentInput = context.CurrentInput;
        var nextStepNo = context.StartStepNo;
        var totalCostDelta = 0m;

        for (var turn = context.StartTurn; turn < run.MaxTurns; turn++)
        {
            if (HasReachedMaxCost(run, totalCostDelta))
            {
                return CreateMaxCostExceededResult(nextStepNo, run, totalCostDelta);
            }

            if (context.StartTurn == 0
                && TryUseHandoffFirstRoutingStrategy(resolvedDefinition.Version.RoutingPolicy)
                && await FindAutomaticRouteRuleAsync(
                    routeRuleRepository,
                    resolvedDefinition.Version.Id,
                    run.AgentCode,
                    currentInput,
                    cancellationToken) is { } automaticRouteRule)
            {
                EnsureHandoffAllowed(automaticRouteRule.TargetAgentCode, resolvedDefinition, run.AgentCode);
                return await CreatePendingHandoffAsync(
                    run,
                    nextStepNo,
                    currentInput,
                    automaticRouteRule.TargetAgentCode,
                    automaticRouteRule.HandoffMode,
                    agentStepRepository,
                    automaticRouteRule,
                    automaticRouteRule.ApprovalRequired,
                    $"Matched route rule '{automaticRouteRule.RuleName}'.",
                    null,
                    null,
                    null,
                    null,
                    cancellationToken,
                    null,
                    totalCostDelta);
            }

            var runtimeContext = runtimeContextComposer is null
                ? new RuntimeContextComposition(currentInput)
                : await runtimeContextComposer.ComposeModelInputAsync(
                    context with { CurrentInput = currentInput },
                    resolvedDefinition,
                    cancellationToken);
            var modelInput = runtimeContext.ModelInput;
            var modelTools = await LoadModelToolsAsync(resolvedDefinition, toolDefinitionRepository, cancellationToken);
            var startedAt = DateTime.UtcNow;
            var modelResponse = await modelGateway.GenerateTextAsync(
                new GenerateTextRequest(
                    version.DefaultModel,
                    BuildRuntimePrompt(version.SystemPromptTemplate),
                    modelInput,
                    OutputMode: string.IsNullOrWhiteSpace(version.OutputSchema)
                        ? null
                        : GenerateTextOutputModes.JsonSchema,
                    OutputSchema: version.OutputSchema,
                    Tools: modelTools),
                cancellationToken);
            var endedAt = DateTime.UtcNow;
            totalCostDelta += NormalizeCost(modelResponse.Cost);
            var completedModelStepNo = nextStepNo;

            await agentStepRepository.AddAsync(CreateStep(
                run.RunId, nextStepNo++, "model_call", "Completed",
                modelInput, modelResponse.Output, null,
                startedAt, endedAt) with
            {
                DecisionPayload = ModelCallPayloadSerializer.Create(version.DefaultModel, modelResponse, runtimeContext.Retrieval)
            }, cancellationToken);
            if (onEvent is not null)
            {
                var modelCallPayload = ModelCallPayloadSerializer.Create(version.DefaultModel, modelResponse, runtimeContext.Retrieval);
                await onEvent(new AgentRunEvent(
                    run.RunId,
                    "step",
                    new AgentRunEventData(nextStepNo - 1, "model_call", "Completed", modelResponse.Output, null, modelCallPayload)));
            }

            if (HasExceededMaxCost(run, totalCostDelta))
            {
                return CreateMaxCostExceededResult(completedModelStepNo, run, totalCostDelta);
            }

            var decision = stepDecisionParser.Parse(modelResponse.Output);

            if (string.Equals(decision.Action, "respond", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(decision.Response))
                    throw new InvalidOperationException("Respond decision did not include a response.");

                var response = decision.Response.Trim();
                return new AgentLoopCompleted(
                    AppendKnowledgeCitations(response, modelInput, resolvedDefinition.Version.ContextPolicy),
                    modelInput,
                    totalCostDelta);
            }

            if (string.Equals(decision.Action, "retrieve", StringComparison.OrdinalIgnoreCase))
            {
                var retrievalQuery = string.IsNullOrWhiteSpace(decision.RetrievalQuery)
                    ? currentInput
                    : decision.RetrievalQuery.Trim();
                var retrievalPayload = RetrievalPayloadSerializer.Create(retrievalQuery);
                var retrievalStep = CreateStep(
                    run.RunId,
                    nextStepNo++,
                    "retrieval",
                    "Completed",
                    retrievalQuery,
                    retrievalQuery,
                    null,
                    DateTime.UtcNow,
                    DateTime.UtcNow) with
                {
                    DecisionPayload = retrievalPayload
                };
                await agentStepRepository.AddAsync(retrievalStep, cancellationToken);

                if (onEvent is not null)
                {
                    await onEvent(new AgentRunEvent(
                        run.RunId,
                        "step",
                        new AgentRunEventData(
                            retrievalStep.StepNo,
                            retrievalStep.StepType,
                            "Completed",
                            retrievalQuery,
                            DecisionPayload: retrievalPayload)));
                }

                currentInput = BuildRetrievalFollowUpInput(currentInput, retrievalQuery);
                continue;
            }

            if (string.Equals(decision.Action, "handoff", StringComparison.OrdinalIgnoreCase))
            {
                var targetAgent = decision.TargetAgent;
                if (string.IsNullOrWhiteSpace(targetAgent))
                {
                    throw new InvalidOperationException("Handoff decision did not include a target agent.");
                }

                var normalizedTargetAgent = targetAgent.Trim();
                EnsureHandoffAllowed(normalizedTargetAgent, resolvedDefinition, run.AgentCode);
                var matchedRouteRule = await FindMatchingRouteRuleAsync(
                    routeRuleRepository,
                    resolvedDefinition.Version.Id,
                    run.AgentCode,
                    normalizedTargetAgent,
                    cancellationToken);

                var handoffMode = NormalizeHandoffMode(
                    string.IsNullOrWhiteSpace(decision.HandoffMode)
                        ? matchedRouteRule?.HandoffMode
                        : decision.HandoffMode);
                var approvalRequired = decision.HandoffApprovalRequired
                    ?? matchedRouteRule?.ApprovalRequired
                    ?? false;
                if (!string.Equals(handoffMode, "delegate_and_wait", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(handoffMode, "route_only", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(handoffMode, "delegate_and_merge", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Handoff mode '{handoffMode}' is not supported yet.");
                }

                var handoffInput = string.IsNullOrWhiteSpace(decision.HandoffInput)
                    ? currentInput
                    : decision.HandoffInput.Trim();
                return await CreatePendingHandoffAsync(
                    run,
                    nextStepNo,
                    handoffInput,
                    normalizedTargetAgent,
                    handoffMode,
                    agentStepRepository,
                    matchedRouteRule,
                    approvalRequired,
                    decision.HandoffReason,
                    decision.HandoffContextOverrides,
                    decision.HandoffMemoryOverrides,
                    decision.HandoffToolOverrides,
                    decision.HandoffKnowledgeOverrides,
                    cancellationToken,
                    decision.HandoffConfidence,
                    totalCostDelta,
                    decision.HandoffMergeStrategy);
            }

            if (string.Equals(decision.Action, "request_human", StringComparison.OrdinalIgnoreCase))
            {
                var comment = string.IsNullOrWhiteSpace(decision.HumanComment)
                    ? "Human assistance required."
                    : decision.HumanComment.Trim();
                return new AgentLoopWaitingHuman(
                    Guid.NewGuid().ToString("N"),
                    nextStepNo,
                    null,
                    null,
                    null,
                    null,
                    null,
                    comment,
                    "run",
                    null,
                    false,
                    totalCostDelta);
            }

            if (string.Equals(decision.Action, "request_approval", StringComparison.OrdinalIgnoreCase))
            {
                var requestedAction = string.IsNullOrWhiteSpace(decision.ApprovalRequestedAction)
                    ? throw new InvalidOperationException("Approval decision did not include a requested action.")
                    : decision.ApprovalRequestedAction.Trim();
                var normalizedSideEffectLevel = string.IsNullOrWhiteSpace(decision.ApprovalSideEffectLevel)
                    ? HandoffApprovalDefaults.SideEffectLevel
                    : ToolDefinitionPolicyValidator.NormalizeSideEffectLevel(
                        decision.ApprovalSideEffectLevel,
                        nameof(decision.ApprovalSideEffectLevel));
                var waitToken = Guid.NewGuid().ToString("N");
                var approvalPayload = ApprovalPayloadSerializer.CreatePending(
                    requestedAction,
                    decision.ApprovalRequestPayload,
                    normalizedSideEffectLevel,
                    decision.ApprovalComment);
                var pendingStep = CreateStep(
                    run.RunId,
                    nextStepNo,
                    "approval_request",
                    "Pending",
                    decision.ApprovalRequestPayload,
                    null,
                    null,
                    DateTime.UtcNow,
                    DateTime.UtcNow) with
                {
                    DecisionPayload = ApprovalPayloadSerializer.Serialize(approvalPayload)
                };

                await agentStepRepository.AddAsync(pendingStep, cancellationToken);
                return new AgentLoopWaitingApproval(
                    waitToken,
                    nextStepNo,
                    pendingStep.StepId,
                    requestedAction,
                    decision.ApprovalRequestPayload,
                    normalizedSideEffectLevel,
                    "approval_request",
                    totalCostDelta);
            }

            if (string.Equals(decision.Action, "fail", StringComparison.OrdinalIgnoreCase))
            {
                var errorMessage = string.IsNullOrWhiteSpace(decision.FailMessage)
                    ? "The model reported a failure."
                    : decision.FailMessage.Trim();
                var errorPayload = ModelFailurePayloadSerializer.Create(decision.FailErrorCode, errorMessage);
                var failedAt = DateTime.UtcNow;
                var failedStep = CreateStep(
                    run.RunId,
                    nextStepNo++,
                    "failed",
                    "Failed",
                    modelInput,
                    null,
                    errorPayload,
                    failedAt,
                    failedAt);
                await agentStepRepository.AddAsync(failedStep, cancellationToken);

                if (onEvent is not null)
                {
                    await onEvent(new AgentRunEvent(
                        run.RunId,
                        "step",
                        new AgentRunEventData(failedStep.StepNo, failedStep.StepType, "Failed", Error: errorPayload)));
                }

                return new AgentLoopFailed(failedStep.StepNo, failedStep.StepType, errorPayload, errorMessage, totalCostDelta);
            }

            if (!string.Equals(decision.Action, "tool_call", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported decision action '{decision.Action}'.");

            var toolName = decision.ToolName!;
            EnsureToolAllowed(toolName, resolvedDefinition, run.AgentCode);

            var toolDefinition = await toolDefinitionRepository.GetByToolNameAsync(toolName, cancellationToken);
            var approvalRequirement = RequiresApproval(toolDefinition, decision.ToolInput, approvalPolicyOptions);
            if (approvalRequirement.RequiresApproval)
            {
                var waitToken = Guid.NewGuid().ToString("N");
                var approvalPayload = ApprovalPayloadSerializer.CreatePending(
                    toolName,
                    decision.ToolInput,
                    approvalRequirement.SideEffectLevel ?? toolDefinition!.SideEffectLevel);
                var pendingStep = CreateStep(
                    run.RunId, nextStepNo, "tool_call", "Pending",
                    decision.ToolInput, null, null,
                    DateTime.UtcNow, DateTime.UtcNow) with
                {
                    DecisionPayload = ApprovalPayloadSerializer.Serialize(approvalPayload)
                };

                await agentStepRepository.AddAsync(pendingStep, cancellationToken);
                return new AgentLoopWaitingApproval(
                    waitToken,
                    nextStepNo,
                    pendingStep.StepId,
                    toolName,
                    decision.ToolInput,
                    approvalRequirement.SideEffectLevel ?? toolDefinition!.SideEffectLevel,
                    "tool_call",
                    totalCostDelta);
            }

            var reusablePendingInvocation = await FindReusablePendingInvocationAsync(
                run.RunId,
                toolName,
                decision.ToolInput,
                toolDefinition,
                toolInvocationRepository,
                agentStepRepository,
                cancellationToken);
            if (reusablePendingInvocation is not null)
            {
                return new AgentLoopSuspended(
                    reusablePendingInvocation.WaitToken,
                    reusablePendingInvocation.StepNo,
                    reusablePendingInvocation.StepId,
                    reusablePendingInvocation.InvocationId,
                    toolName,
                    decision.ToolInput,
                    totalCostDelta);
            }

            var reusableInvocation = await FindReusableCompletedInvocationAsync(
                run.RunId,
                toolName,
                decision.ToolInput,
                toolDefinition,
                toolInvocationRepository,
                cancellationToken);
            var toolStartedAt = DateTime.UtcNow;
            ToolExecutionResult toolResult;
            try
            {
                toolResult = reusableInvocation is null
                    ? await toolExecutor.ExecuteAsync(
                        toolName,
                        decision.ToolInput,
                        new ToolExecutionContext(run.RunId, run.AgentCode, context.CurrentInput),
                        cancellationToken)
                    : ToolExecutionResult.Completed(toolName, reusableInvocation.OutputPayload!);
            }
            catch (InvalidOperationException ex)
            {
                var failedAt = DateTime.UtcNow;
                var errorPayload = ToolFailurePayloadSerializer.Create(
                    toolName,
                    "execution",
                    ex.Message,
                    toolDefinition?.CompensationPolicy);
                var failedStep = CreateStep(
                    run.RunId, nextStepNo++, "tool_call", "Failed",
                    decision.ToolInput, null, errorPayload,
                    toolStartedAt, failedAt);
                await agentStepRepository.AddAsync(failedStep, cancellationToken);

                var failedInvocationId = Guid.NewGuid().ToString("N");
                await toolInvocationRepository.AddAsync(
                    CreateToolInvocation(
                        failedInvocationId,
                        run.RunId,
                        failedStep.StepId,
                        toolName,
                        "sync",
                        "Failed",
                        decision.ToolInput,
                        null,
                        errorPayload,
                        failedInvocationId,
                        string.Empty,
                        toolStartedAt,
                        failedAt,
                        failedAt),
                    cancellationToken);

                if (onEvent is not null)
                {
                    await onEvent(new AgentRunEvent(
                        run.RunId,
                        "step",
                        new AgentRunEventData(failedStep.StepNo, "tool_call", "Failed", Error: errorPayload)));
                }

                if (ShouldWaitForManualCompensation(toolDefinition))
                {
                    return new AgentLoopWaitingHuman(
                        Guid.NewGuid().ToString("N"),
                        nextStepNo,
                        failedStep.StepId,
                        failedInvocationId,
                        toolName,
                        decision.ToolInput,
                        errorPayload,
                        BuildManualCompensationComment(toolName),
                        "tool_failure",
                        "Failed",
                        true,
                        totalCostDelta);
                }

                return new AgentLoopFailed(failedStep.StepNo, "tool_call", errorPayload, ex.Message, totalCostDelta);
            }
            var toolEndedAt = DateTime.UtcNow;

            if (toolResult.IsFailed)
            {
                var errorMessage = toolResult.Error ?? toolResult.Output;
                var errorPayload = ToolFailurePayloadSerializer.Create(
                    toolName,
                    "execution",
                    errorMessage,
                    toolDefinition?.CompensationPolicy);
                var failedStep = CreateStep(
                    run.RunId, nextStepNo++, "tool_call", "Failed",
                    decision.ToolInput, null, errorPayload,
                    toolStartedAt, toolEndedAt);
                await agentStepRepository.AddAsync(failedStep, cancellationToken);

                var failedInvocationId = Guid.NewGuid().ToString("N");
                await toolInvocationRepository.AddAsync(
                    CreateToolInvocation(
                        failedInvocationId,
                        run.RunId,
                        failedStep.StepId,
                        toolName,
                        reusableInvocation is null ? "sync" : "reused",
                        "Failed",
                        decision.ToolInput,
                        null,
                        errorPayload,
                        reusableInvocation?.IdempotencyKey ?? failedInvocationId,
                        string.Empty,
                        toolStartedAt,
                        toolEndedAt,
                        toolEndedAt),
                    cancellationToken);

                if (onEvent is not null)
                {
                    await onEvent(new AgentRunEvent(
                        run.RunId,
                        "step",
                        new AgentRunEventData(failedStep.StepNo, "tool_call", "Failed", Error: errorPayload)));
                }

                if (ShouldWaitForManualCompensation(toolDefinition))
                {
                    return new AgentLoopWaitingHuman(
                        Guid.NewGuid().ToString("N"),
                        nextStepNo,
                        failedStep.StepId,
                        failedInvocationId,
                        toolName,
                        decision.ToolInput,
                        errorPayload,
                        BuildManualCompensationComment(toolName),
                        "tool_failure",
                        "Failed",
                        true,
                        totalCostDelta);
                }

                return new AgentLoopFailed(failedStep.StepNo, "tool_call", errorPayload, errorMessage, totalCostDelta);
            }

            if (toolResult.IsPending)
            {
                var waitToken = toolResult.WaitToken ?? Guid.NewGuid().ToString("N");
                var pendingStep = CreateStep(
                    run.RunId, nextStepNo, "tool_call", "Pending",
                    decision.ToolInput, null, null,
                    toolStartedAt, toolEndedAt) with
                {
                    DecisionPayload = SerializePendingToolMetadata(toolName)
                };
                await agentStepRepository.AddAsync(pendingStep, cancellationToken);

                var invocationId = Guid.NewGuid().ToString("N");
                await toolInvocationRepository.AddAsync(
                    CreateToolInvocation(
                        invocationId,
                        run.RunId,
                        pendingStep.StepId,
                        toolName,
                        "async",
                        "Pending",
                        decision.ToolInput,
                        null,
                        null,
                        invocationId,
                        waitToken,
                        toolStartedAt,
                        null,
                        toolEndedAt),
                    cancellationToken);

                return new AgentLoopSuspended(
                    waitToken,
                    nextStepNo,
                    pendingStep.StepId,
                    invocationId,
                    toolName,
                    decision.ToolInput,
                    totalCostDelta);
            }

            var completedStep = CreateStep(
                run.RunId, nextStepNo++, "tool_call", "Completed",
                decision.ToolInput, toolResult.Output, null,
                toolStartedAt, toolEndedAt);
            await agentStepRepository.AddAsync(completedStep, cancellationToken);

            var completedInvocationId = Guid.NewGuid().ToString("N");
            await toolInvocationRepository.AddAsync(
                CreateToolInvocation(
                    completedInvocationId,
                    run.RunId,
                    completedStep.StepId,
                    toolName,
                    reusableInvocation is null ? "sync" : "reused",
                    "Completed",
                    decision.ToolInput,
                    toolResult.Output,
                    null,
                    reusableInvocation?.IdempotencyKey ?? completedInvocationId,
                    string.Empty,
                    toolStartedAt,
                    toolEndedAt,
                    toolEndedAt),
                cancellationToken);

            currentInput = BuildToolFollowUpInput(context.CurrentInput, toolResult);
        }

        throw new InvalidOperationException("Max turns exceeded.");
    }

    private static async Task<PendingToolReuse?> FindReusablePendingInvocationAsync(
        string runId,
        string toolName,
        string? toolInput,
        ToolDefinition? toolDefinition,
        IToolInvocationRepository toolInvocationRepository,
        IAgentStepRepository agentStepRepository,
        CancellationToken cancellationToken)
    {
        if (toolDefinition is null
            || !ToolIdempotencyPolicyHelper.IsEnabled(toolName, toolDefinition.IdempotencyPolicy))
        {
            return null;
        }

        var invocations = await toolInvocationRepository.ListByRunIdAsync(runId, cancellationToken);
        foreach (var invocation in invocations
                     .Where(invocation =>
                         string.Equals(invocation.ToolName, toolName, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(invocation.Status, "Pending", StringComparison.OrdinalIgnoreCase)
                         && string.Equals(invocation.InputPayload, toolInput, StringComparison.Ordinal)
                         && !string.IsNullOrWhiteSpace(invocation.CallbackToken))
                     .OrderByDescending(invocation => invocation.CreateTime))
        {
            var step = await agentStepRepository.GetByStepIdAsync(invocation.StepId, cancellationToken);
            if (step is null
                || !string.Equals(step.RunId, runId, StringComparison.Ordinal)
                || !string.Equals(step.StepType, "tool_call", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(step.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new PendingToolReuse(
                invocation.CallbackToken,
                step.StepNo,
                step.StepId,
                invocation.InvocationId);
        }

        return null;
    }

    private static async Task<ToolInvocation?> FindReusableCompletedInvocationAsync(
        string runId,
        string toolName,
        string? toolInput,
        ToolDefinition? toolDefinition,
        IToolInvocationRepository toolInvocationRepository,
        CancellationToken cancellationToken)
    {
        if (toolDefinition is null
            || !ToolIdempotencyPolicyHelper.IsEnabled(toolName, toolDefinition.IdempotencyPolicy))
        {
            return null;
        }

        var invocations = await toolInvocationRepository.ListByRunIdAsync(runId, cancellationToken);
        return invocations
            .Where(invocation =>
                string.Equals(invocation.ToolName, toolName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(invocation.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                && string.Equals(invocation.InputPayload, toolInput, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(invocation.OutputPayload))
            .OrderByDescending(invocation => invocation.CreateTime)
            .FirstOrDefault();
    }

    private static string BuildRuntimePrompt(string? systemPromptTemplate)
    {
        if (string.IsNullOrWhiteSpace(systemPromptTemplate))
            return RuntimeInstruction;

        return $"{systemPromptTemplate.Trim()}\n\n{RuntimeInstruction}";
    }

    private static string BuildToolFollowUpInput(string originalInput, ToolExecutionResult toolResult)
    {
        return
            $"""
            Original user input:
            {originalInput}

            Tool called:
            {toolResult.ToolName}

            Tool result:
            {toolResult.Output}

            Produce the final user-facing answer now.
            """;
    }

    private static string AppendKnowledgeCitations(string response, string modelInput, string? contextPolicy)
    {
        if (string.IsNullOrWhiteSpace(response)
            || !ShouldAppendKnowledgeCitations(contextPolicy)
            || response.Contains("References:", StringComparison.OrdinalIgnoreCase))
        {
            return response;
        }

        var citations = ExtractKnowledgeCitations(modelInput);
        if (citations.Count == 0)
        {
            return response;
        }

        var builder = new StringBuilder(response.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("References:");
        foreach (var citation in citations)
        {
            builder.AppendLine(citation);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildRetrievalFollowUpInput(string originalInput, string retrievalQuery)
    {
        return
            $"""
            Original user input:
            {originalInput}

            Retrieval query:
            {retrievalQuery}

            Use the retrieved knowledge to continue planning and answer the user.
            """;
    }

    public static string BuildApprovalFollowUpInput(
        string originalInput,
        string requestedAction,
        string? requestPayload,
        string sideEffectLevel,
        string? comment)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Original user input:");
        builder.AppendLine(originalInput);
        builder.AppendLine();
        builder.AppendLine("Approval granted for:");
        builder.AppendLine(requestedAction);
        builder.AppendLine();
        builder.AppendLine($"Side effect level: {sideEffectLevel}");

        if (!string.IsNullOrWhiteSpace(requestPayload))
        {
            builder.AppendLine();
            builder.AppendLine("Approval request payload:");
            builder.AppendLine(requestPayload.Trim());
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            builder.AppendLine();
            builder.AppendLine("Approval note:");
            builder.AppendLine(comment.Trim());
        }

        builder.AppendLine();
        builder.AppendLine("Use the approval result to continue planning and answer the user.");
        return builder.ToString().Trim();
    }

    private static bool ShouldAppendKnowledgeCitations(string? contextPolicy)
    {
        if (string.IsNullOrWhiteSpace(contextPolicy))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(contextPolicy);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("citations", out var citationsElement))
            {
                return true;
            }

            return citationsElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(citationsElement.GetString(), out var value) => value,
                _ => true
            };
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static IReadOnlyList<string> ExtractKnowledgeCitations(string modelInput)
    {
        if (string.IsNullOrWhiteSpace(modelInput))
        {
            return Array.Empty<string>();
        }

        var normalized = modelInput.Replace("\r\n", "\n", StringComparison.Ordinal);
        var marker = "Reference knowledge:\n";
        var startIndex = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return Array.Empty<string>();
        }

        var lines = normalized[(startIndex + marker.Length)..].Split('\n');
        var citations = new List<string>();
        string? pendingIndex = null;
        string? pendingCitation = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith('[')
                && line.Contains(']'))
            {
                pendingIndex = line[..(line.IndexOf(']') + 1)];
                pendingCitation = null;
                continue;
            }

            if (line.StartsWith("Citation:", StringComparison.OrdinalIgnoreCase))
            {
                pendingCitation = line["Citation:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("Source:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pendingIndex)
                && !string.IsNullOrWhiteSpace(pendingCitation))
            {
                var source = line["Source:".Length..].Trim();
                citations.Add($"{pendingIndex} {source} ({pendingCitation})");
                pendingIndex = null;
                pendingCitation = null;
                continue;
            }

            if (line.EndsWith(':') && !line.StartsWith("Source:", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return citations;
    }

    private static ApprovalRequirement RequiresApproval(
        ToolDefinition? toolDefinition,
        string? toolInput,
        ApprovalPolicyOptions? approvalPolicyOptions)
    {
        return ApprovalPolicyRules.EvaluateApprovalRequirement(toolDefinition, toolInput, approvalPolicyOptions);
    }

    private static bool ShouldWaitForManualCompensation(ToolDefinition? toolDefinition)
    {
        return toolDefinition is not null
            && ToolCompensationPolicyHelper.IsManual(toolDefinition.CompensationPolicy);
    }

    private static string BuildManualCompensationComment(string toolName)
    {
        return $"Tool '{toolName}' failed and requires manual compensation.";
    }

    private static async Task<IReadOnlyList<GenerateTextToolDefinition>> LoadModelToolsAsync(
        ResolvedAgentDefinition resolvedDefinition,
        IToolDefinitionRepository toolDefinitionRepository,
        CancellationToken cancellationToken)
    {
        var allowedTools = AgentDefinitionToolListSerializer.Parse(resolvedDefinition.Version.AllowedTools);
        if (allowedTools.Count == 0)
        {
            return Array.Empty<GenerateTextToolDefinition>();
        }

        var tools = new List<GenerateTextToolDefinition>();
        foreach (var toolName in allowedTools
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var definition = await toolDefinitionRepository.GetByToolNameAsync(toolName, cancellationToken);
            if (definition is null || !definition.Enabled)
            {
                continue;
            }

            tools.Add(new GenerateTextToolDefinition(
                definition.ToolName,
                definition.Description,
                definition.InputSchema));
        }

        return tools;
    }

    private static string SerializePendingToolMetadata(string toolName)
    {
        return JsonSerializer.Serialize(new
        {
            toolName
        });
    }

    private static void EnsureToolAllowed(string toolName, ResolvedAgentDefinition resolvedDefinition, string agentCode)
    {
        var allowedTools = AgentDefinitionToolListSerializer.Parse(resolvedDefinition.Version.AllowedTools);
        var deniedTools = AgentDefinitionToolListSerializer.Parse(resolvedDefinition.Version.DeniedTools);
        if (deniedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Tool '{toolName}' is denied for agent definition '{agentCode}'.");
        if (!allowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Tool '{toolName}' is not allowed for agent definition '{agentCode}'.");
    }

    private static void EnsureHandoffAllowed(string targetAgent, ResolvedAgentDefinition resolvedDefinition, string agentCode)
    {
        var allowedHandoffs = AgentDefinitionToolListSerializer.Parse(resolvedDefinition.Version.AllowedHandoffs);
        if (!allowedHandoffs.Contains(targetAgent, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Handoff target '{targetAgent}' is not allowed for agent definition '{agentCode}'.");
        }
    }

    private static async Task<RouteRule?> FindMatchingRouteRuleAsync(
        IRouteRuleRepository? routeRuleRepository,
        string agentDefinitionVersionId,
        string sourceAgentCode,
        string targetAgentCode,
        CancellationToken cancellationToken)
    {
        if (routeRuleRepository is null || string.IsNullOrWhiteSpace(agentDefinitionVersionId))
        {
            return null;
        }

        var routeRules = await routeRuleRepository.GetByAgentDefinitionVersionIdAsync(agentDefinitionVersionId, cancellationToken);
        return routeRules
            .Where(routeRule => routeRule.Enabled)
            .Where(routeRule =>
                string.IsNullOrWhiteSpace(routeRule.SourceAgentCode)
                || string.Equals(routeRule.SourceAgentCode, sourceAgentCode, StringComparison.OrdinalIgnoreCase))
            .Where(routeRule => string.Equals(routeRule.TargetAgentCode, targetAgentCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(routeRule => routeRule.Priority)
            .ThenBy(routeRule => routeRule.RuleName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static async Task<RouteRule?> FindAutomaticRouteRuleAsync(
        IRouteRuleRepository? routeRuleRepository,
        string agentDefinitionVersionId,
        string sourceAgentCode,
        string currentInput,
        CancellationToken cancellationToken)
    {
        if (routeRuleRepository is null
            || string.IsNullOrWhiteSpace(agentDefinitionVersionId)
            || string.IsNullOrWhiteSpace(currentInput))
        {
            return null;
        }

        var routeRules = await routeRuleRepository.GetByAgentDefinitionVersionIdAsync(agentDefinitionVersionId, cancellationToken);
        return routeRules
            .Where(routeRule => routeRule.Enabled)
            .Where(routeRule =>
                string.IsNullOrWhiteSpace(routeRule.SourceAgentCode)
                || string.Equals(routeRule.SourceAgentCode, sourceAgentCode, StringComparison.OrdinalIgnoreCase))
            .Where(routeRule => IsAutomaticRouteRuleMatch(routeRule, currentInput))
            .OrderBy(routeRule => routeRule.Priority)
            .ThenBy(routeRule => routeRule.RuleName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static async Task<AgentLoopResult> CreatePendingHandoffAsync(
        AgentRun run,
        int stepNo,
        string handoffInput,
        string targetAgent,
        string handoffMode,
        IAgentStepRepository agentStepRepository,
        RouteRule? matchedRouteRule,
        bool approvalRequired,
        string? reason,
        string? contextOverrides,
        string? memoryOverrides,
        string? toolOverrides,
        string? knowledgeOverrides,
        CancellationToken cancellationToken,
        double? confidence = null,
        decimal totalCostDelta = 0m,
        string? mergeStrategy = null)
    {
        var handoffWaitToken = Guid.NewGuid().ToString("N");
        var childRunId = Guid.NewGuid().ToString("N");
        var effectiveMergeStrategy = mergeStrategy ?? matchedRouteRule?.MergeStrategy;
        var pendingStep = CreateStep(
            run.RunId,
            stepNo,
            "handoff",
            "Pending",
            handoffInput,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow) with
        {
            DecisionPayload = HandoffPayloadSerializer.Serialize(
                HandoffPayloadSerializer.CreatePending(
                    handoffWaitToken,
                    targetAgent,
                    handoffInput,
                    handoffMode,
                    childRunId,
                    matchedRouteRule?.Id,
                    matchedRouteRule?.ContextScope,
                    matchedRouteRule?.MemoryScope,
                    matchedRouteRule?.ToolScope,
                    matchedRouteRule?.KnowledgeScope,
                    approvalRequired,
                    reason,
                    confidence,
                    contextOverrides,
                    memoryOverrides,
                    toolOverrides,
                    knowledgeOverrides,
                    effectiveMergeStrategy))
        };

        await agentStepRepository.AddAsync(pendingStep, cancellationToken);
        if (approvalRequired)
        {
            return new AgentLoopWaitingApproval(
                Guid.NewGuid().ToString("N"),
                stepNo,
                pendingStep.StepId,
                targetAgent,
                handoffInput,
                HandoffApprovalDefaults.SideEffectLevel,
                "handoff",
                totalCostDelta);
        }

        return new AgentLoopWaitingHandoff(
            handoffWaitToken,
            stepNo,
            pendingStep.StepId,
            targetAgent,
            handoffInput,
            handoffMode,
            childRunId,
            totalCostDelta,
            HandoffPayloadSerializer.NormalizeMergeStrategy(handoffMode, effectiveMergeStrategy));
    }

    private static AgentLoopFailed CreateMaxCostExceededResult(int failedAtStepNo, AgentRun run, decimal totalCostDelta)
    {
        var message = $"Run cost {run.TotalCost + totalCostDelta:0.######} exceeded the configured maximum {run.MaxCost:0.######}.";
        return new AgentLoopFailed(failedAtStepNo, "model_call", message, message, totalCostDelta);
    }

    private static bool HasReachedMaxCost(AgentRun run, decimal totalCostDelta)
        => run.MaxCost > 0m && run.TotalCost + totalCostDelta >= run.MaxCost;

    private static bool HasExceededMaxCost(AgentRun run, decimal totalCostDelta)
        => run.MaxCost > 0m && run.TotalCost + totalCostDelta > run.MaxCost;

    private static decimal NormalizeCost(decimal cost)
        => cost < 0m ? 0m : cost;

    private static bool TryUseHandoffFirstRoutingStrategy(string? routingPolicy)
    {
        if (string.IsNullOrWhiteSpace(routingPolicy))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(routingPolicy);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("strategy", out var strategyElement)
                || strategyElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return string.Equals(strategyElement.GetString(), "handoff-first", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsAutomaticRouteRuleMatch(RouteRule routeRule, string currentInput)
    {
        if (string.IsNullOrWhiteSpace(routeRule.MatchType))
        {
            return false;
        }

        return routeRule.MatchType.Trim().ToLowerInvariant() switch
        {
            "intent" => MatchRouteTerms(routeRule.MatchExpression, currentInput),
            "keyword" => MatchRouteTerms(routeRule.MatchExpression, currentInput),
            "regex" => MatchRouteRegex(routeRule.MatchExpression, currentInput),
            _ => false
        };
    }

    private static bool MatchRouteTerms(string? matchExpression, string currentInput)
    {
        if (string.IsNullOrWhiteSpace(matchExpression)
            || string.IsNullOrWhiteSpace(currentInput))
        {
            return false;
        }

        var normalizedInput = currentInput.Trim();

        try
        {
            using var document = JsonDocument.Parse(matchExpression);
            return IsRouteMatch(document.RootElement, normalizedInput);
        }
        catch (JsonException)
        {
            return normalizedInput.Contains(matchExpression.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsRouteMatch(JsonElement root, string normalizedInput)
    {
        if (root.ValueKind == JsonValueKind.String)
        {
            var single = root.GetString();
            return !string.IsNullOrWhiteSpace(single)
                && normalizedInput.Contains(single.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var requiredTerms = ReadRouteMatchTerms(root, "intent", "keyword", "contains", "all");
        if (requiredTerms.Count != 0
            && requiredTerms.Any(term => !normalizedInput.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var optionalTerms = ReadRouteMatchTerms(root, "any", "keywords", "terms");
        if (optionalTerms.Count != 0)
        {
            return optionalTerms.Any(term =>
                normalizedInput.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return requiredTerms.Count != 0;
    }

    private static bool MatchRouteRegex(string? matchExpression, string currentInput)
    {
        if (string.IsNullOrWhiteSpace(matchExpression)
            || string.IsNullOrWhiteSpace(currentInput))
        {
            return false;
        }

        var pattern = ExtractRouteRegexPattern(matchExpression);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(
                currentInput,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? ExtractRouteRegexPattern(string matchExpression)
    {
        try
        {
            using var document = JsonDocument.Parse(matchExpression);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString();
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ReadRouteMatchString(root, "pattern")
                ?? ReadRouteMatchString(root, "regex")
                ?? ReadRouteMatchString(root, "expression");
        }
        catch (JsonException)
        {
            return matchExpression.Trim();
        }
    }

    private static string? ReadRouteMatchString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static IReadOnlyList<string> ReadRouteMatchTerms(JsonElement root, params string[] propertyNames)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        var candidates = new List<string>();
        foreach (var propertyName in propertyNames)
        {
            AddRouteMatchTerm(root, propertyName, candidates);
            AddRouteMatchTerms(root, propertyName, candidates);
        }

        return candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddRouteMatchTerm(JsonElement root, string propertyName, ICollection<string> values)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var value = property.GetString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static void AddRouteMatchTerms(JsonElement root, string propertyName, ICollection<string> values)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in property.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }
    }

    private static string NormalizeHandoffMode(string? mode)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? "delegate_and_wait" : mode.Trim();
        return normalized switch
        {
            "delegate_and_wait" => "delegate_and_wait",
            "route_only" => "route_only",
            "delegate_and_merge" => "delegate_and_merge",
            _ => throw new InvalidOperationException($"Unsupported handoff mode '{normalized}'.")
        };
    }

    public static AgentStep CreateStep(
        string runId, int stepNo, string stepType, string status,
        string? input, string? output, string? error,
        DateTime startedAt, DateTime endedAt)
    {
        return new AgentStep
        {
            StepId = Guid.NewGuid().ToString("N"),
            RunId = runId,
            StepNo = stepNo,
            StepType = stepType,
            Status = status,
            InputPayload = input,
            OutputPayload = output,
            ErrorPayload = error,
            StepKey = $"{runId}:{stepType}:{stepNo}",
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationMs = Math.Max(0, (long)(endedAt - startedAt).TotalMilliseconds),
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = startedAt,
            LastModifyTime = endedAt
        };
    }

    public static ToolInvocation CreateToolInvocation(
        string invocationId,
        string runId,
        string stepId,
        string toolName,
        string mode,
        string status,
        string? input,
        string? output,
        string? error,
        string idempotencyKey,
        string callbackToken,
        DateTime startedAt,
        DateTime? endedAt,
        DateTime observedAt)
    {
        return new ToolInvocation
        {
            InvocationId = invocationId,
            RunId = runId,
            StepId = stepId,
            ToolName = toolName,
            Mode = mode,
            Status = status,
            InputPayload = input,
            OutputPayload = output,
            ErrorPayload = error,
            IdempotencyKey = idempotencyKey,
            CallbackToken = callbackToken,
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationMs = Math.Max(0, (long)((endedAt ?? observedAt) - startedAt).TotalMilliseconds),
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = startedAt,
            LastModifyTime = endedAt ?? observedAt
        };
    }

    private sealed record PendingToolReuse(
        string WaitToken,
        int StepNo,
        string StepId,
        string InvocationId);
}
