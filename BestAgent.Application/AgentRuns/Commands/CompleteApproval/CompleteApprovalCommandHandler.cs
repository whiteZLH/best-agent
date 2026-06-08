using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CompleteApproval;

public class CompleteApprovalCommandHandler : IRequestHandler<CompleteApprovalCommand, CompleteApprovalResult>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentApprovalRepository _agentApprovalRepository;
    private readonly IAgentRunChannel _agentRunChannel;
    private readonly IApprovalAuthorizer _approvalAuthorizer;
    private readonly IIdempotencyRecordRepository _idempotencyRecordRepository;
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;

    public CompleteApprovalCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentApprovalRepository agentApprovalRepository,
        IAgentRunChannel agentRunChannel,
        IApprovalAuthorizer approvalAuthorizer,
        IIdempotencyRecordRepository idempotencyRecordRepository,
        IAgentDefinitionRepository agentDefinitionRepository)
    {
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentApprovalRepository = agentApprovalRepository;
        _agentRunChannel = agentRunChannel;
        _approvalAuthorizer = approvalAuthorizer;
        _idempotencyRecordRepository = idempotencyRecordRepository;
        _agentDefinitionRepository = agentDefinitionRepository;
    }

    public async Task<CompleteApprovalResult> Handle(CompleteApprovalCommand request, CancellationToken cancellationToken)
    {
        var decision = NormalizeDecision(request.Decision);
        var normalizedIdempotencyKey = Normalize(request.IdempotencyKey);
        var idempotencyScopeKey = normalizedIdempotencyKey is null
            ? null
            : BuildScopeKey(request.RunId, request.ApprovalId, normalizedIdempotencyKey);
        var requestHash = BuildRequestHash(
            decision,
            request.ApproverId,
            request.ApproverName,
            request.ApproverRole,
            request.Comment);

        if (idempotencyScopeKey is not null)
        {
            var existingRecord = await _idempotencyRecordRepository.GetByScopeAsync(
                ScopeTypes.ApprovalComplete,
                idempotencyScopeKey,
                cancellationToken);
            if (existingRecord is not null)
            {
                if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new ConflictException("Idempotency key was already used with a different approval completion payload.");
                }

                var replayedResult = DeserializeResult(existingRecord.ExtraPayload);
                if (replayedResult is not null)
                {
                    return replayedResult;
                }
            }
        }

        var approval = await _agentApprovalRepository.GetByApprovalIdAsync(request.ApprovalId, cancellationToken);
        if (approval is null || approval.RunId != request.RunId)
        {
            throw new NotFoundException("AgentApproval", request.ApprovalId);
        }

        if (!string.Equals(approval.Decision, ApprovalDecisions.Pending, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Approval '{request.ApprovalId}' is already '{approval.Decision}'.");
        }

        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
        {
            throw new NotFoundException("AgentRun", request.RunId);
        }

        if (agentRun.Status != "WaitingApproval")
        {
            throw new ConflictException($"Run '{request.RunId}' is in status '{agentRun.Status}', expected 'WaitingApproval'.");
        }

        var pendingStep = await _agentStepRepository.GetLastByRunIdAsync(request.RunId, cancellationToken);
        if (pendingStep is null || pendingStep.StepId != approval.StepId || pendingStep.Status != "Pending")
        {
            throw new ConflictException($"Approval '{request.ApprovalId}' is not the current pending approval for run '{request.RunId}'.");
        }

        if (!PendingApprovalContextParser.TryParsePending(pendingStep, out var approvalContext))
        {
            throw new ConflictException($"Step '{approval.StepId}' is not waiting for approval.");
        }

        var resolvedDefinition = await ResolveDefinitionForRunAsync(agentRun, cancellationToken);
        _approvalAuthorizer.Authorize(new ApprovalAuthorizationContext(
            request.RunId,
            approval.StepId,
            approvalContext!.RequestedAction,
            approvalContext.SideEffectLevel,
            request.ApproverId,
            request.ApproverName,
            request.ApproverRole,
            resolvedDefinition?.Version.ApprovalPolicy,
            await AgentRunApprovalPolicyResolver.ResolveEffectivePolicyAsync(
                _agentDefinitionRepository,
                _agentRunRepository,
                _agentStepRepository,
                agentRun,
                cancellationToken)));

        agentRun = agentRun with
        {
            Status = "Running",
            CurrentWaitToken = string.Empty,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = DateTime.UtcNow
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

        if (decision == ApprovalDecisions.Approved)
        {
            await _agentRunChannel.EnqueueAsync(
                new ApproveAgentRunStepMessage(
                    request.RunId,
                    approval.StepId,
                    request.ApproverId,
                    request.ApproverName,
                    request.ApproverRole,
                    request.Comment),
                cancellationToken);
        }
        else
        {
            await _agentRunChannel.EnqueueAsync(
                new RejectAgentRunStepMessage(
                    request.RunId,
                    approval.StepId,
                    request.Comment,
                    request.ApproverId,
                    request.ApproverName,
                    request.ApproverRole),
                cancellationToken);
        }

        var result = new CompleteApprovalResult(
            agentRun.RunId,
            agentRun.AgentCode,
            agentRun.InputPayload,
            null,
            "Running",
            decision);

        if (idempotencyScopeKey is not null)
        {
            await _idempotencyRecordRepository.AddAsync(
                CreateIdempotencyRecord(
                    idempotencyScopeKey,
                    requestHash,
                    request.ApprovalId,
                    result),
                cancellationToken);
        }

        return result;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildScopeKey(string runId, string approvalId, string idempotencyKey)
    {
        return BuildHash($"{runId}:{approvalId}:{idempotencyKey}");
    }

    private static string BuildRequestHash(
        string decision,
        string? approverId,
        string? approverName,
        string? approverRole,
        string? comment)
    {
        return BuildHash(JsonSerializer.Serialize(new
        {
            decision,
            approverId,
            approverName,
            approverRole,
            comment
        }));
    }

    private static string BuildHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static CompleteApprovalResult? DeserializeResult(string? payload)
    {
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize<CompleteApprovalResult>(payload);
    }

    private static IdempotencyRecord CreateIdempotencyRecord(
        string scopeKey,
        string requestHash,
        string approvalId,
        CompleteApprovalResult result)
    {
        var now = DateTime.UtcNow;
        return new IdempotencyRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            ScopeType = ScopeTypes.ApprovalComplete,
            ScopeKey = scopeKey,
            RequestHash = requestHash,
            TargetId = approvalId,
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

    private static string NormalizeDecision(string decision)
    {
        if (string.Equals(decision, ApprovalDecisions.Approved, StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalDecisions.Approved;
        }

        if (string.Equals(decision, ApprovalDecisions.Rejected, StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalDecisions.Rejected;
        }

        throw new ConflictException("Approval decision must be 'Approved' or 'Rejected'.");
    }

    private static class ScopeTypes
    {
        public const string ApprovalComplete = "approval_complete";
    }

    private async Task<ResolvedAgentDefinition?> ResolveDefinitionForRunAsync(AgentRun agentRun, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(agentRun.AgentDefinitionVersionId))
        {
            var boundDefinition = await _agentDefinitionRepository.GetByVersionIdAsync(
                agentRun.AgentDefinitionVersionId,
                cancellationToken);
            if (boundDefinition is not null)
            {
                return boundDefinition;
            }
        }

        return await _agentDefinitionRepository.GetEnabledByCodeAsync(agentRun.AgentCode, cancellationToken);
    }
}
