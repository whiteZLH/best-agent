using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Application.Observability;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

public class CreateAgentRunCommandHandler : IRequestHandler<CreateAgentRunCommand, CreateAgentRunResult>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentRunChannel _agentRunChannel;
    private readonly IAgentMetrics _agentMetrics;

    public CreateAgentRunCommandHandler(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentRunChannel agentRunChannel,
        IAgentMetrics agentMetrics)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentRunChannel = agentRunChannel;
        _agentMetrics = agentMetrics;
    }

    public async Task<CreateAgentRunResult> Handle(CreateAgentRunCommand request, CancellationToken cancellationToken)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        if (idempotencyKey is not null)
        {
            var existingRun = await _agentRunRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
            if (existingRun is not null)
            {
                return ToResult(existingRun);
            }
        }

        var resolvedDefinition = await _agentDefinitionRepository.GetEnabledByCodeAsync(request.AgentCode, cancellationToken);
        if (resolvedDefinition is null)
            throw new NotFoundException("AgentDefinition", request.AgentCode);

        if (request.MaxRounds is <= 0)
        {
            throw new InvalidOperationException("Options.MaxRounds must be greater than zero.");
        }

        var now = DateTime.UtcNow;
        var runId = Guid.NewGuid().ToString("N");
        idempotencyKey ??= runId;
        var tenantId = NormalizeOptionalValue(request.TenantId);
        var userId = NormalizeOptionalValue(request.UserId);
        var sessionId = NormalizeOptionalValue(request.SessionId);
        var effectiveMaxTurns = request.MaxRounds is int requestedMaxRounds
            ? Math.Min(requestedMaxRounds, resolvedDefinition.Version.MaxTurns)
            : resolvedDefinition.Version.MaxTurns;
        var agentRun = BuildAgentRun(
            runId,
            request.AgentCode,
            resolvedDefinition.Version.Id,
            request.Input,
            idempotencyKey,
            tenantId,
            userId,
            sessionId,
            effectiveMaxTurns,
            resolvedDefinition.Version.MaxCost,
            now);

        await _agentRunRepository.AddAsync(agentRun, cancellationToken);
        await _agentStepRepository.AddAsync(AgentRunLoop.CreateStep(runId, 1, "created", "Completed", request.Input, null, null, now, now), cancellationToken);
        await _agentStepRepository.AddAsync(AgentRunLoop.CreateStep(runId, 2, "running", "Completed", request.Input, null, null, now, now), cancellationToken);
        _agentMetrics.RecordRunCreated(request.AgentCode, isChildRun: false);

        await _agentRunChannel.EnqueueAsync(new CreateAgentRunMessage(runId), cancellationToken);

        return new CreateAgentRunResult(runId, request.AgentCode, request.Input, null, "Running");
    }

    public static AgentRun BuildAgentRun(
        string runId,
        string agentCode,
        string agentDefinitionVersionId,
        string? input,
        string idempotencyKey,
        string? tenantId,
        string? userId,
        string? sessionId,
        int maxTurns,
        decimal maxCost,
        DateTime now,
        string? parentRunId = null,
        string? rootRunId = null,
        string? delegatedByRunId = null,
        string? delegatedByAgent = null)
    {
        return new AgentRun
        {
            RunId = runId,
            AgentCode = agentCode,
            AgentDefinitionVersionId = agentDefinitionVersionId,
            TenantId = tenantId ?? string.Empty,
            UserId = userId ?? string.Empty,
            SessionId = sessionId ?? string.Empty,
            Status = "Running",
            InputPayload = input,
            ParentRunId = parentRunId ?? string.Empty,
            RootRunId = rootRunId ?? runId,
            DelegatedByRunId = delegatedByRunId ?? string.Empty,
            DelegatedByAgent = delegatedByAgent ?? string.Empty,
            IdempotencyKey = idempotencyKey,
            MaxTurns = maxTurns,
            MaxCost = maxCost,
            StartedAt = now,
            StatusVersion = 1,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        return string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static CreateAgentRunResult ToResult(AgentRun agentRun)
    {
        return new CreateAgentRunResult(
            agentRun.RunId,
            agentRun.AgentCode,
            agentRun.InputPayload,
            agentRun.OutputPayload,
            agentRun.Status,
            string.IsNullOrWhiteSpace(agentRun.CurrentWaitToken) ? null : agentRun.CurrentWaitToken);
    }
}
