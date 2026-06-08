using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CompleteToolInvocation;

public class CompleteToolInvocationCommandHandler : IRequestHandler<CompleteToolInvocationCommand, CompleteToolInvocationResult>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentRunChannel _agentRunChannel;
    private readonly IIdempotencyRecordRepository _idempotencyRecordRepository;
    private readonly IToolInvocationRepository _toolInvocationRepository;

    public CompleteToolInvocationCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentRunChannel agentRunChannel,
        IIdempotencyRecordRepository idempotencyRecordRepository,
        IToolInvocationRepository toolInvocationRepository)
    {
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentRunChannel = agentRunChannel;
        _idempotencyRecordRepository = idempotencyRecordRepository;
        _toolInvocationRepository = toolInvocationRepository;
    }

    public async Task<CompleteToolInvocationResult> Handle(CompleteToolInvocationCommand request, CancellationToken cancellationToken)
    {
        var normalizedIdempotencyKey = Normalize(request.IdempotencyKey);
        var idempotencyScopeKey = normalizedIdempotencyKey is null
            ? null
            : BuildScopeKey(request.RunId, request.InvocationId, normalizedIdempotencyKey);
        var requestHash = BuildRequestHash(request.WaitToken, request.ToolResult);

        if (idempotencyScopeKey is not null)
        {
            var existingRecord = await _idempotencyRecordRepository.GetByScopeAsync(
                ScopeTypes.ToolInvocationComplete,
                idempotencyScopeKey,
                cancellationToken);
            if (existingRecord is not null)
            {
                if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new ConflictException("Idempotency key was already used with a different tool completion payload.");
                }

                var replayedResult = DeserializeResult(existingRecord.ExtraPayload);
                if (replayedResult is not null)
                {
                    return replayedResult;
                }
            }
        }

        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
        {
            throw new NotFoundException("AgentRun", request.RunId);
        }

        if (agentRun.Status != "WaitingTool")
        {
            throw new ConflictException($"Run '{request.RunId}' is in status '{agentRun.Status}', expected 'WaitingTool'.");
        }

        if (agentRun.CurrentWaitToken != request.WaitToken)
        {
            throw new ConflictException($"Wait token mismatch for run '{request.RunId}'.");
        }

        var invocation = await _toolInvocationRepository.GetByInvocationIdAsync(request.InvocationId, cancellationToken);
        if (invocation is null
            || !string.Equals(invocation.RunId, request.RunId, StringComparison.Ordinal)
            || !string.Equals(invocation.Status, "Pending", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(invocation.CallbackToken, request.WaitToken, StringComparison.Ordinal))
        {
            throw new ConflictException($"Tool invocation '{request.InvocationId}' is not the current pending invocation for run '{request.RunId}'.");
        }

        var pendingStep = await _agentStepRepository.GetLastByRunIdAsync(request.RunId, cancellationToken);
        if (pendingStep is null
            || pendingStep.Status != "Pending"
            || pendingStep.StepId != invocation.StepId)
        {
            throw new ConflictException($"Tool invocation '{request.InvocationId}' is not the current pending invocation for run '{request.RunId}'.");
        }

        agentRun = agentRun with
        {
            Status = "Running",
            CurrentWaitToken = string.Empty,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = DateTime.UtcNow
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

        await _agentRunChannel.EnqueueAsync(
            new ResumeAgentRunMessage(request.RunId, request.WaitToken, request.ToolResult, request.InvocationId),
            cancellationToken);

        var result = new CompleteToolInvocationResult(agentRun.RunId, agentRun.AgentCode, agentRun.InputPayload, null, "Running");
        if (idempotencyScopeKey is not null)
        {
            await _idempotencyRecordRepository.AddAsync(
                CreateIdempotencyRecord(
                    idempotencyScopeKey,
                    requestHash,
                    request.InvocationId,
                    result),
                cancellationToken);
        }

        return result;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildScopeKey(string runId, string invocationId, string idempotencyKey)
    {
        return BuildHash($"{runId}:{invocationId}:{idempotencyKey}");
    }

    private static string BuildRequestHash(string waitToken, string toolResult)
    {
        return BuildHash(JsonSerializer.Serialize(new { waitToken, toolResult }));
    }

    private static string BuildHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static CompleteToolInvocationResult? DeserializeResult(string? payload)
    {
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize<CompleteToolInvocationResult>(payload);
    }

    private static IdempotencyRecord CreateIdempotencyRecord(
        string scopeKey,
        string requestHash,
        string invocationId,
        CompleteToolInvocationResult result)
    {
        var now = DateTime.UtcNow;
        return new IdempotencyRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            ScopeType = ScopeTypes.ToolInvocationComplete,
            ScopeKey = scopeKey,
            RequestHash = requestHash,
            TargetId = invocationId,
            Status = "completed",
            ExpireAt = now.AddDays(7),
            ExtraPayload = JsonSerializer.Serialize(result),
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }
    private static class ScopeTypes
    {
        public const string ToolInvocationComplete = "tool_complete";
    }
}
