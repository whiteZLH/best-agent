using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

public class GetAgentRunStepsQueryHandler : IRequestHandler<GetAgentRunStepsQuery, IReadOnlyList<GetAgentRunStepsItem>>
{
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IAgentApprovalRepository _agentApprovalRepository;
    private readonly IToolInvocationRepository _toolInvocationRepository;

    public GetAgentRunStepsQueryHandler(
        IAgentStepRepository agentStepRepository,
        IAgentApprovalRepository agentApprovalRepository,
        IToolInvocationRepository toolInvocationRepository)
    {
        _agentStepRepository = agentStepRepository;
        _agentApprovalRepository = agentApprovalRepository;
        _toolInvocationRepository = toolInvocationRepository;
    }

    public async Task<IReadOnlyList<GetAgentRunStepsItem>> Handle(GetAgentRunStepsQuery request, CancellationToken cancellationToken)
    {
        var steps = await _agentStepRepository.ListByRunIdAsync(request.RunId, cancellationToken);
        var approvals = await _agentApprovalRepository.ListByRunIdAsync(request.RunId, cancellationToken);
        var invocations = await _toolInvocationRepository.ListByRunIdAsync(request.RunId, cancellationToken);
        var approvalsByStepId = approvals.ToDictionary(x => x.StepId, StringComparer.Ordinal);
        var invocationsByStepId = invocations
            .GroupBy(x => x.StepId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(invocation => invocation.CreateTime).First(),
                StringComparer.Ordinal);

        return steps
            .Select(step => new GetAgentRunStepsItem(
                step.StepId,
                step.StepNo,
                step.StepType,
                step.Status,
                MaskStepInput(step.StepType, step.InputPayload),
                MaskStepOutput(step.StepType, step.OutputPayload),
                step.ErrorPayload,
                step.StepKey,
                AgentRunStepDataMapper.MapHandoff(step.DecisionPayload),
                MapApproval(step.StepId, step.DecisionPayload, approvalsByStepId),
                AgentRunStepDataMapper.MapHumanWait(step.DecisionPayload),
                MapToolInvocation(step.StepId, invocationsByStepId),
                MapModelCall(step.DecisionPayload),
                MapModelFailure(step.ErrorPayload),
                MapToolFailure(step.ErrorPayload),
                step.CreateTime,
                step.LastModifyTime,
                step.StartedAt,
                step.EndedAt,
                step.DurationMs))
            .ToList();
    }

    private static ToolInvocationInfo? MapToolInvocation(
        string stepId,
        IReadOnlyDictionary<string, ToolInvocation> invocationsByStepId)
    {
        return invocationsByStepId.TryGetValue(stepId, out var invocation)
            ? AgentRunStepDataMapper.MapToolInvocation(invocation)
            : null;
    }

    private static ApprovalInfo? MapApproval(
        string stepId,
        string? decisionPayload,
        IReadOnlyDictionary<string, AgentApproval> approvalsByStepId)
    {
        approvalsByStepId.TryGetValue(stepId, out var approval);
        return AgentRunStepDataMapper.MapApproval(approval, decisionPayload);
    }

    private static ModelCallInfo? MapModelCall(string? decisionPayload)
    {
        if (!ModelCallPayloadSerializer.TryParse(decisionPayload, out var payload))
        {
            return null;
        }

        return new ModelCallInfo(
            payload!.Model,
            payload.PromptTokens,
            payload.CompletionTokens,
            payload.TotalTokens,
            payload.Cost,
            payload.Retrieval is null
                ? null
                : new ModelCallRetrievalInfo(
                    payload.Retrieval.QueryText,
                    payload.Retrieval.WasRewritten,
                    payload.Retrieval.CandidateCount,
                    payload.Retrieval.SelectedCount,
                    payload.Retrieval.RequestedSources,
                    payload.Retrieval.SelectedSources,
                    payload.Retrieval.Citations));
    }

    private static ModelFailureInfo? MapModelFailure(string? errorPayload)
    {
        if (!ModelFailurePayloadSerializer.TryParse(errorPayload, out var payload))
        {
            return null;
        }

        return new ModelFailureInfo(
            payload!.ErrorCode,
            payload.Message);
    }

    private static ToolFailureInfo? MapToolFailure(string? errorPayload)
    {
        if (!ToolFailurePayloadSerializer.TryParse(errorPayload, out var payload))
        {
            return null;
        }

        return new ToolFailureInfo(
            payload!.ToolName,
            payload.Stage,
            payload.Message,
            string.IsNullOrWhiteSpace(payload.Compensation?.Mode)
                ? null
                : new ToolFailureCompensationInfo(payload.Compensation.Mode));
    }

    private static string? MaskStepInput(string stepType, string? inputPayload)
    {
        return string.Equals(stepType, "tool_call", StringComparison.OrdinalIgnoreCase)
            ? RuntimePayloadMasker.MaskToolInput(inputPayload)
            : inputPayload;
    }

    private static string? MaskStepOutput(string stepType, string? outputPayload)
    {
        return string.Equals(stepType, "tool_call", StringComparison.OrdinalIgnoreCase)
            ? RuntimePayloadMasker.MaskToolOutput(outputPayload)
            : outputPayload;
    }
}
