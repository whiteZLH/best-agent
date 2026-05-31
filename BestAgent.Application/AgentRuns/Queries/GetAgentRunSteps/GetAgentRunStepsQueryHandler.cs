using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public class GetAgentRunStepsQueryHandler : IRequestHandler<GetAgentRunStepsQuery, IReadOnlyList<GetAgentRunStepsItem>>
{
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentApprovalRepository _agentApprovalRepository;

    public GetAgentRunStepsQueryHandler(
        IAgentStepRepository agentStepRepository,
        IAgentApprovalRepository agentApprovalRepository)
    {
        _agentStepRepository = agentStepRepository;
        _agentApprovalRepository = agentApprovalRepository;
    }

    public async Task<IReadOnlyList<GetAgentRunStepsItem>> Handle(GetAgentRunStepsQuery request, CancellationToken cancellationToken)
    {
        var steps = await _agentStepRepository.ListByRunIdAsync(request.RunId, cancellationToken);
        var approvals = await _agentApprovalRepository.ListByRunIdAsync(request.RunId, cancellationToken);
        var approvalsByStepId = approvals.ToDictionary(x => x.StepId, StringComparer.Ordinal);

        return steps
            .Select(step => new GetAgentRunStepsItem(
                step.StepId,
                step.StepNo,
                step.StepType,
                step.Status,
                step.InputPayload,
                step.OutputPayload,
                step.ErrorPayload,
                step.StepKey,
                MapApproval(step.StepId, step.DecisionPayload, approvalsByStepId),
                step.CreateTime,
                step.LastModifyTime,
                step.StartedAt,
                step.EndedAt,
                step.DurationMs))
            .ToList();
    }

    private static ApprovalInfo? MapApproval(
        string stepId,
        string? decisionPayload,
        IReadOnlyDictionary<string, AgentApproval> approvalsByStepId)
    {
        if (approvalsByStepId.TryGetValue(stepId, out var approval))
        {
            return new ApprovalInfo(
                "approval",
                approval.RequestedAction,
                approval.RequestPayload,
                approval.RiskLevel,
                approval.Decision,
                string.IsNullOrWhiteSpace(approval.Comment) ? null : approval.Comment,
                approval.DecidedAt,
                approval.ApprovalId,
                string.IsNullOrWhiteSpace(approval.ApproverId) ? null : approval.ApproverId,
                string.IsNullOrWhiteSpace(approval.ApproverName) ? null : approval.ApproverName,
                string.IsNullOrWhiteSpace(approval.ApproverRole) ? null : approval.ApproverRole);
        }

        if (!ApprovalPayloadSerializer.TryParse(decisionPayload, out var payload))
        {
            return null;
        }

        return new ApprovalInfo(
            payload!.WaitType,
            payload.ToolName,
            payload.ToolInput,
            payload.SideEffectLevel,
            payload.Decision,
            payload.Comment,
            payload.DecidedAt);
    }
}
