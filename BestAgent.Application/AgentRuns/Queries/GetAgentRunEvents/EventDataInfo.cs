using System.Text.Json;
using BestAgent.Application.AgentRuns.Runtime;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;

public record EventDataInfo(
    int StepNo,
    string StepType,
    string Status,
    string? Output,
    string? Error,
    EventModelCallInfo? ModelCall,
    EventRetrievalInfo? Retrieval,
    EventModelFailureInfo? ModelFailure,
    EventToolFailureInfo? ToolFailure,
    EventToolInvocationInfo? ToolInvocation = null,
    EventApprovalInfo? Approval = null,
    EventHandoffInfo? Handoff = null,
    EventHumanWaitInfo? HumanWait = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static EventDataInfo? FromPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var data = JsonSerializer.Deserialize<AgentRunEventData>(payload, JsonOptions);
            if (data is null)
            {
                return null;
            }

            return FromRuntimeData(data);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static EventDataInfo FromRuntimeData(AgentRunEventData data)
    {
        return new EventDataInfo(
            data.StepNo,
            data.StepType,
            data.Status,
            data.Output,
            data.Error,
            ModelCallPayloadSerializer.TryParse(data.ModelCall, out var modelCall)
                ? new EventModelCallInfo(
                    modelCall!.Model,
                    modelCall.PromptTokens,
                    modelCall.CompletionTokens,
                    modelCall.TotalTokens,
                    modelCall.Cost,
                    modelCall.FinishReason,
                    modelCall.Retrieval is null
                        ? null
                        : new EventModelCallRetrievalInfo(
                            modelCall.Retrieval.QueryText,
                            modelCall.Retrieval.WasRewritten,
                            modelCall.Retrieval.CandidateCount,
                            modelCall.Retrieval.SelectedCount,
                            modelCall.Retrieval.RequestedSources,
                            modelCall.Retrieval.SelectedSources,
                            modelCall.Retrieval.Citations),
                    modelCall.ReasoningSummary)
                : null,
            RetrievalPayloadSerializer.TryParse(data.DecisionPayload, out var retrieval)
                ? new EventRetrievalInfo(retrieval!.QueryText)
                : null,
            ModelFailurePayloadSerializer.TryParse(data.Error, out var modelFailure)
                ? new EventModelFailureInfo(modelFailure!.ErrorCode, modelFailure.Message)
                : null,
            ToolFailurePayloadSerializer.TryParse(data.Error, out var toolFailure)
                ? new EventToolFailureInfo(
                    toolFailure!.ToolName,
                    toolFailure.Stage,
                    toolFailure.Message,
                    string.IsNullOrWhiteSpace(toolFailure.Compensation?.Mode)
                        ? null
                        : new EventToolFailureCompensationInfo(toolFailure.Compensation.Mode))
                : null,
            MapToolInvocation(data.ToolInvocation),
            MapApproval(data.DecisionPayload),
            MapHandoff(data.DecisionPayload),
            MapHumanWait(data.DecisionPayload));
    }

    private static EventToolInvocationInfo? MapToolInvocation(string? toolInvocationPayload)
    {
        if (!ToolInvocationEventPayloadSerializer.TryParse(toolInvocationPayload, out var payload))
        {
            return null;
        }

        return new EventToolInvocationInfo(
            payload!.InvocationId,
            payload.ToolName,
            payload.Mode,
            payload.Status,
            payload.CallbackToken);
    }

    private static EventApprovalInfo? MapApproval(string? decisionPayload)
    {
        if (!ApprovalPayloadSerializer.TryParse(decisionPayload, out var payload))
        {
            return null;
        }

        return new EventApprovalInfo(
            payload!.WaitType,
            payload.ToolName,
            RuntimePayloadMasker.MaskToolInput(payload.ToolInput),
            payload.SideEffectLevel,
            payload.Decision,
            payload.Comment,
            payload.DecidedAt);
    }

    private static EventHandoffInfo? MapHandoff(string? decisionPayload)
    {
        if (!HandoffPayloadSerializer.TryParse(decisionPayload, out var payload))
        {
            return null;
        }

        return new EventHandoffInfo(
            payload!.WaitType,
            payload.TargetAgent,
            payload.HandoffInput,
            payload.Mode,
            payload.ChildRunId,
            payload.Decision,
            payload.ChildStatus,
            payload.ChildOutput,
            payload.Comment,
            payload.DecidedAt,
            payload.RouteRuleId,
            payload.ContextScope,
            payload.MemoryScope,
            payload.ToolScope,
            payload.KnowledgeScope,
            payload.ApprovalRequired,
            payload.Reason,
            payload.Confidence,
            payload.ContextOverrides,
            payload.MemoryOverrides,
            payload.ToolOverrides,
            payload.KnowledgeOverrides,
            payload.MergeStrategy);
    }

    private static EventHumanWaitInfo? MapHumanWait(string? decisionPayload)
    {
        if (!HumanApprovalPayloadSerializer.TryParse(decisionPayload, out var payload))
        {
            return null;
        }

        return new EventHumanWaitInfo(
            payload!.WaitType,
            payload.Decision,
            payload.Comment,
            payload.DecidedAt,
            payload.HumanOperatorId,
            payload.HumanOperatorName,
            payload.HumanOperatorRole,
            payload.HumanResult,
            payload.SourceType,
            payload.SourceStepId,
            payload.SourceInvocationId,
            payload.SourceToolName,
            RuntimePayloadMasker.MaskToolInput(payload.SourceToolInput),
            RuntimePayloadMasker.MaskToolOutput(payload.SourceToolOutput),
            payload.SourceToolStatus,
            payload.ContinueAsToolResult);
    }
}

public record EventModelCallInfo(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    string? FinishReason,
    EventModelCallRetrievalInfo? Retrieval,
    string? ReasoningSummary = null);

public record EventModelCallRetrievalInfo(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);

public record EventRetrievalInfo(
    string QueryText);

public record EventModelFailureInfo(
    string? ErrorCode,
    string Message);

public record EventToolFailureInfo(
    string ToolName,
    string Stage,
    string Message,
    EventToolFailureCompensationInfo? Compensation);

public record EventToolFailureCompensationInfo(
    string Mode);

public record EventToolInvocationInfo(
    string InvocationId,
    string ToolName,
    string Mode,
    string Status,
    string CallbackToken);

public record EventApprovalInfo(
    string WaitType,
    string RequestedAction,
    string? RequestPayload,
    string SideEffectLevel,
    string Decision,
    string? Comment,
    DateTime? DecidedAt)
{
    public string ToolName => RequestedAction;

    public string? ToolInput => RequestPayload;
}

public record EventHandoffInfo(
    string WaitType,
    string TargetAgent,
    string? HandoffInput,
    string Mode,
    string ChildRunId,
    string Decision,
    string? ChildStatus,
    string? ChildOutput,
    string? Comment,
    DateTime? DecidedAt,
    string? RouteRuleId,
    string? ContextScope,
    string? MemoryScope,
    string? ToolScope,
    string? KnowledgeScope,
    bool ApprovalRequired,
    string? Reason,
    double? Confidence,
    string? ContextOverrides,
    string? MemoryOverrides,
    string? ToolOverrides,
    string? KnowledgeOverrides,
    string? MergeStrategy = null);

public record EventHumanWaitInfo(
    string WaitType,
    string Decision,
    string? Comment,
    DateTime? DecidedAt,
    string? HumanOperatorId,
    string? HumanOperatorName,
    string? HumanOperatorRole,
    string? HumanResult,
    string? SourceType = null,
    string? SourceStepId = null,
    string? SourceInvocationId = null,
    string? SourceToolName = null,
    string? SourceToolInput = null,
    string? SourceToolOutput = null,
    string? SourceToolStatus = null,
    bool ContinueAsToolResult = false);
