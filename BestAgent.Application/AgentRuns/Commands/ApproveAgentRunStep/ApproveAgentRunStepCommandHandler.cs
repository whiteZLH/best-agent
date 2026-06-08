using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;

public class ApproveAgentRunStepCommandHandler : IRequestHandler<ApproveAgentRunStepCommand, ApproveAgentRunStepResult>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentRunChannel _agentRunChannel;
    private readonly IApprovalAuthorizer _approvalAuthorizer;
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly ApprovalPolicyOptions _approvalPolicyOptions;
    private readonly TenantApprovalPolicyOptions _tenantApprovalPolicyOptions;

    public ApproveAgentRunStepCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IAgentRunChannel agentRunChannel,
        IApprovalAuthorizer approvalAuthorizer,
        IAgentDefinitionRepository agentDefinitionRepository,
        ApprovalPolicyOptions? approvalPolicyOptions = null,
        TenantApprovalPolicyOptions? tenantApprovalPolicyOptions = null)
    {
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _agentRunChannel = agentRunChannel;
        _approvalAuthorizer = approvalAuthorizer;
        _agentDefinitionRepository = agentDefinitionRepository;
        _approvalPolicyOptions = ApprovalPolicyOptionsNormalizer.Normalize(approvalPolicyOptions);
        _tenantApprovalPolicyOptions = TenantApprovalPolicyOptionsNormalizer.Normalize(tenantApprovalPolicyOptions);
    }

    public async Task<ApproveAgentRunStepResult> Handle(ApproveAgentRunStepCommand request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
            throw new NotFoundException("AgentRun", request.RunId);

        if (agentRun.Status != "WaitingApproval")
            throw new ConflictException($"Run '{request.RunId}' is in status '{agentRun.Status}', expected 'WaitingApproval'.");

        var pendingStep = await _agentStepRepository.GetLastByRunIdAsync(request.RunId, cancellationToken);
        if (pendingStep is null || pendingStep.StepId != request.StepId || pendingStep.Status != "Pending")
            throw new ConflictException($"Step '{request.StepId}' is not the current pending approval step for run '{request.RunId}'.");

        if (!PendingApprovalContextParser.TryParsePending(pendingStep, out var approvalContext))
            throw new ConflictException($"Step '{request.StepId}' is not waiting for approval.");

        var resolvedDefinition = await ResolveDefinitionForRunAsync(agentRun, cancellationToken);
        _approvalAuthorizer.Authorize(new ApprovalAuthorizationContext(
            RunId: request.RunId,
            StepId: request.StepId,
            ToolName: approvalContext!.RequestedAction,
            SideEffectLevel: approvalContext.SideEffectLevel,
            ApproverId: request.ApproverId,
            ApproverName: request.ApproverName,
            ApproverRole: request.ApproverRole,
            ApprovalPolicyOptions: await AgentRunApprovalPolicyResolver.ResolveEffectivePolicyAsync(
                _agentDefinitionRepository,
                _agentRunRepository,
                _agentStepRepository,
                agentRun,
                _approvalPolicyOptions,
                _tenantApprovalPolicyOptions,
                cancellationToken)));

        agentRun = agentRun with
        {
            Status = "Running",
            CurrentWaitToken = string.Empty,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = DateTime.UtcNow
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

        await _agentRunChannel.EnqueueAsync(
            new ApproveAgentRunStepMessage(
                request.RunId,
                request.StepId,
                request.ApproverId,
                request.ApproverName,
                request.ApproverRole,
                request.Comment),
            cancellationToken);

        return new ApproveAgentRunStepResult(agentRun.RunId, agentRun.AgentCode, agentRun.InputPayload, null, "Running");
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
