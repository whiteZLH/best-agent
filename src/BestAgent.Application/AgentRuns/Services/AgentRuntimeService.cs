using System.Text.Json;
using BestAgent.Application.Abstractions;
using BestAgent.Application.Common;
using BestAgent.Application.Planning;
using BestAgent.Domain.Agents;
using BestAgent.Domain.Events;
using BestAgent.Domain.Idempotency;
using BestAgent.Domain.Messages;
using BestAgent.Domain.Runs;
using BestAgent.Domain.Steps;
using BestAgent.Domain.Tools;

namespace BestAgent.Application.AgentRuns.Services;

public sealed class AgentRuntimeService
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentMessageRepository _agentMessageRepository;
    private readonly IIdempotencyRecordRepository _idempotencyRecordRepository;
    private readonly IOutboxEventRepository _outboxEventRepository;
    private readonly IToolInvocationRepository _toolInvocationRepository;
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolExecutor _toolExecutor;
    private readonly IModelGateway _modelGateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ContextBuilder _contextBuilder;
    private readonly PlanValidator _planValidator;

    public AgentRuntimeService(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentMessageRepository agentMessageRepository,
        IIdempotencyRecordRepository idempotencyRecordRepository,
        IOutboxEventRepository outboxEventRepository,
        IToolInvocationRepository toolInvocationRepository,
        IToolRegistry toolRegistry,
        IToolExecutor toolExecutor,
        IModelGateway modelGateway,
        IUnitOfWork unitOfWork,
        ContextBuilder contextBuilder,
        PlanValidator planValidator)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentMessageRepository = agentMessageRepository;
        _idempotencyRecordRepository = idempotencyRecordRepository;
        _outboxEventRepository = outboxEventRepository;
        _toolInvocationRepository = toolInvocationRepository;
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _modelGateway = modelGateway;
        _unitOfWork = unitOfWork;
        _contextBuilder = contextBuilder;
        _planValidator = planValidator;
    }

    public async Task<AgentRunModel> CreateAsync(RuntimeRequest request, CancellationToken cancellationToken)
    {
        var existing = await _agentRunRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ToModel(existing);
        }

        var definition = await LoadDefinitionAsync(request.AgentCode, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var audit = AuditStamp.System(now);
        var run = new AgentRun
        {
            RunId = IdGenerator.New("run"),
            AgentCode = definition.Code,
            IdempotencyKey = request.IdempotencyKey,
            InputPayload = JsonSerializer.Serialize(new { text = request.InputText }),
            Status = AgentRunStatus.Created,
            CurrentStepNo = 0,
            StatusVersion = 0
        };
        audit.ApplyForCreate(run);

        var idempotencyRecord = new IdempotencyRecord
        {
            Id = IdGenerator.New("idem"),
            IdempotencyKey = request.IdempotencyKey,
            RunId = run.RunId,
            RecordedAt = now
        };
        audit.ApplyForCreate(idempotencyRecord);

        var inputMessage = CreateMessage(run.RunId, "user", request.InputText, audit, now);

        await _agentRunRepository.AddAsync(run, cancellationToken);
        await _idempotencyRecordRepository.AddAsync(idempotencyRecord, cancellationToken);
        await _agentMessageRepository.AddAsync(inputMessage, cancellationToken);
        await AddStepAsync(run, AgentStepType.Input, AgentStepStatus.Succeeded, request.InputText, request.InputText, null, audit, now, cancellationToken);
        await AddOutboxEventAsync(run.RunId, "run.created", new { runId = run.RunId, agentCode = run.AgentCode }, audit, now, cancellationToken);
        run.MoveToRunning(now);
        audit.ApplyForUpdate(run);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await ExecutePlanningLoopAsync(run, definition, audit, cancellationToken);
    }

    public async Task<AgentRunModel> ResumeAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await _agentRunRepository.GetByIdAsync(runId, cancellationToken)
            ?? throw new EntityNotFoundException($"Run {runId} was not found.");

        if (run.Status == AgentRunStatus.Completed)
        {
            throw new InvalidOperationException("Completed run cannot be resumed.");
        }

        var definition = await LoadDefinitionAsync(run.AgentCode, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var audit = AuditStamp.System(now);
        run.MoveToRunning(now);
        audit.ApplyForUpdate(run);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await ExecutePlanningLoopAsync(run, definition, audit, cancellationToken);
    }

    private async Task<AgentRunModel> ExecutePlanningLoopAsync(
        AgentRun run,
        AgentDefinition definition,
        AuditStamp audit,
        CancellationToken cancellationToken)
    {
        try
        {
            var toolCallAllowed = true;

            for (var iteration = 0; iteration < definition.MaxTurns; iteration++)
            {
                var messages = await _agentMessageRepository.ListByRunIdAsync(run.RunId, cancellationToken);
                var context = _contextBuilder.Build(definition, run, messages);
                var decision = await _modelGateway.PlanAsync(context, cancellationToken);
                _planValidator.Validate(decision, toolCallAllowed);

                var decisionPayload = JsonSerializer.Serialize(new
                {
                    type = decision.Type.ToString(),
                    decision.Reason,
                    decision.ResponseMessage,
                    decision.ToolCalls,
                    decision.SelectedModel
                });

                await AddStepAsync(run, AgentStepType.Plan, AgentStepStatus.Succeeded, decision.Reason, decisionPayload, null, audit, DateTimeOffset.UtcNow, cancellationToken);

                if (decision.Type == PlanDecisionType.Respond)
                {
                    var outputText = decision.ResponseMessage!;
                    await _agentMessageRepository.AddAsync(CreateMessage(run.RunId, "assistant", outputText, audit, DateTimeOffset.UtcNow), cancellationToken);
                    await AddStepAsync(run, AgentStepType.Respond, AgentStepStatus.Succeeded, outputText, outputText, null, audit, DateTimeOffset.UtcNow, cancellationToken);
                    run.Complete(JsonSerializer.Serialize(new { text = outputText }), DateTimeOffset.UtcNow);
                    audit.ApplyForUpdate(run);
                    await AddOutboxEventAsync(run.RunId, "run.completed", new { runId = run.RunId, status = run.Status.ToString() }, audit, DateTimeOffset.UtcNow, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return ToModel(run);
                }

                var toolCall = decision.ToolCalls[0];
                EnsureAllowedTool(definition, toolCall.ToolName);

                var invocation = new ToolInvocation
                {
                    InvocationId = IdGenerator.New("toolinv"),
                    RunId = run.RunId,
                    StepId = string.Empty,
                    ToolName = toolCall.ToolName,
                    Status = ToolInvocationStatus.Pending,
                    InputPayload = toolCall.ArgumentsJson,
                    IdempotencyKey = $"{run.RunId}:{toolCall.ToolName}:{iteration}",
                    StartedAt = DateTimeOffset.UtcNow
                };
                audit.ApplyForCreate(invocation);
                var toolCallStep = await AddStepAsync(run, AgentStepType.ToolCall, AgentStepStatus.Succeeded, toolCall.ArgumentsJson, toolCall.ToolName, null, audit, DateTimeOffset.UtcNow, cancellationToken);
                invocation.StepId = toolCallStep.StepId;
                await _toolInvocationRepository.AddAsync(invocation, cancellationToken);

                var toolResult = await _toolExecutor.ExecuteAsync(toolCall.ToolName, toolCall.ArgumentsJson, cancellationToken);
                invocation.Status = toolResult.Error is null ? ToolInvocationStatus.Succeeded : ToolInvocationStatus.Failed;
                invocation.OutputPayload = JsonSerializer.Serialize(toolResult);
                invocation.ErrorPayload = toolResult.Error;
                invocation.EndedAt = DateTimeOffset.UtcNow;
                audit.ApplyForUpdate(invocation);

                var toolMessageContent = JsonSerializer.Serialize(new
                {
                    tool = toolCall.ToolName,
                    result = toolResult
                });
                await _agentMessageRepository.AddAsync(CreateMessage(run.RunId, "tool", toolMessageContent, audit, DateTimeOffset.UtcNow), cancellationToken);
                await AddStepAsync(run, AgentStepType.ToolResult, toolResult.Error is null ? AgentStepStatus.Succeeded : AgentStepStatus.Failed, toolCall.ToolName, toolMessageContent, toolResult.Error, audit, DateTimeOffset.UtcNow, cancellationToken);
                await AddOutboxEventAsync(run.RunId, "step.completed", new { runId = run.RunId, stepType = AgentStepType.ToolResult.ToString() }, audit, DateTimeOffset.UtcNow, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                toolCallAllowed = false;
            }

            throw new InvalidOperationException("Run exceeded max turns without a final response.");
        }
        catch (Exception ex)
        {
            await FailRunAsync(run, ex.Message, audit, cancellationToken);
            return ToModel(run);
        }
    }

    private async Task FailRunAsync(AgentRun run, string errorMessage, AuditStamp audit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await AddStepAsync(run, AgentStepType.Interrupt, AgentStepStatus.Failed, errorMessage, null, errorMessage, audit, now, cancellationToken);
        run.Fail(errorMessage, now);
        audit.ApplyForUpdate(run);
        await AddOutboxEventAsync(run.RunId, "run.failed", new { runId = run.RunId, error = errorMessage }, audit, now, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<AgentDefinition> LoadDefinitionAsync(string agentCode, CancellationToken cancellationToken)
    {
        return await _agentDefinitionRepository.GetByCodeAsync(agentCode, cancellationToken)
            ?? throw new EntityNotFoundException($"Agent definition {agentCode} was not found.");
    }

    private void EnsureAllowedTool(AgentDefinition definition, string toolName)
    {
        var tool = _toolRegistry.Get(toolName);
        if (tool is null || !tool.Enabled)
        {
            throw new InvalidOperationException($"Tool {toolName} is not registered.");
        }

        if (!definition.GetAllowedTools().Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Tool {toolName} is not allowed for agent {definition.Code}.");
        }
    }

    private async Task<AgentStep> AddStepAsync(
        AgentRun run,
        AgentStepType stepType,
        AgentStepStatus status,
        string inputPayload,
        string? outputPayload,
        string? errorPayload,
        AuditStamp audit,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        run.IncrementStep();
        var step = new AgentStep
        {
            StepId = IdGenerator.New("step"),
            RunId = run.RunId,
            StepNo = run.CurrentStepNo,
            StepType = stepType,
            Status = status,
            InputPayload = inputPayload,
            OutputPayload = outputPayload,
            ErrorPayload = errorPayload,
            StepKey = $"{run.RunId}:{run.CurrentStepNo}:{stepType}",
            RetryCount = 0,
            StartedAt = now,
            EndedAt = now
        };
        audit.ApplyForCreate(step);
        await _agentStepRepository.AddAsync(step, cancellationToken);
        audit.ApplyForUpdate(run);
        return step;
    }

    private async Task AddOutboxEventAsync(
        string runId,
        string eventType,
        object payload,
        AuditStamp audit,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var outboxEvent = new OutboxEvent
        {
            EventId = IdGenerator.New("evt"),
            RunId = runId,
            SequenceNo = await _outboxEventRepository.GetNextSequenceAsync(runId, cancellationToken),
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            OccurredAt = now
        };
        audit.ApplyForCreate(outboxEvent);
        await _outboxEventRepository.AddAsync(outboxEvent, cancellationToken);
    }

    private static AgentMessage CreateMessage(string runId, string role, string content, AuditStamp audit, DateTimeOffset now)
    {
        var message = new AgentMessage
        {
            MessageId = IdGenerator.New("msg"),
            RunId = runId,
            Role = role,
            Content = content,
            CreatedAt = now
        };
        audit.ApplyForCreate(message);
        return message;
    }

    private static AgentRunModel ToModel(AgentRun run)
    {
        return new AgentRunModel(
            run.RunId,
            run.AgentCode,
            run.Status.ToString(),
            run.OutputPayload,
            run.ErrorMessage,
            run.CurrentStepNo,
            run.IdempotencyKey);
    }
}
