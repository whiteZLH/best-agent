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
    EventModelFailureInfo? ModelFailure,
    EventToolFailureInfo? ToolFailure)
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
                        modelCall.Retrieval is null
                            ? null
                            : new EventModelCallRetrievalInfo(
                                modelCall.Retrieval.QueryText,
                                modelCall.Retrieval.WasRewritten,
                                modelCall.Retrieval.CandidateCount,
                                modelCall.Retrieval.SelectedCount,
                                modelCall.Retrieval.RequestedSources,
                                modelCall.Retrieval.SelectedSources,
                                modelCall.Retrieval.Citations))
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
                    : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public record EventModelCallInfo(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    EventModelCallRetrievalInfo? Retrieval);

public record EventModelCallRetrievalInfo(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);

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
