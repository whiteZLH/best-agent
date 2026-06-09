using System.Text.Json;
using BestAgent.Application.Models;

namespace BestAgent.Application.AgentRuns.Runtime;

public sealed record ModelCallPayload(
    string Type,
    string Model,
    string? ResponseId,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal Cost,
    string? FinishReason = null,
    string? ServiceTier = null,
    ModelCallRetrievalPayload? Retrieval = null,
    string? ReasoningSummary = null,
    IReadOnlyList<ModelCallToolCallPayload>? ToolCalls = null);

public sealed record ModelCallToolCallPayload(
    string Id,
    string Type,
    string Name,
    string? Arguments = null);

public sealed record ModelCallRetrievalPayload(
    string QueryText,
    bool WasRewritten,
    int CandidateCount,
    int SelectedCount,
    IReadOnlyList<string> RequestedSources,
    IReadOnlyList<string> SelectedSources,
    IReadOnlyList<string> Citations);

public static class ModelCallPayloadSerializer
{
    public static string Create(string model, GenerateTextResult result, RuntimeRetrievalAudit? retrieval = null)
    {
        return JsonSerializer.Serialize(new ModelCallPayload(
            "model_call",
            string.IsNullOrWhiteSpace(model) ? string.Empty : model.Trim(),
            string.IsNullOrWhiteSpace(result.ResponseId) ? null : result.ResponseId.Trim(),
            Math.Max(0, result.PromptTokens),
            Math.Max(0, result.CompletionTokens),
            Math.Max(0, result.TotalTokens),
            result.Cost < 0m ? 0m : result.Cost,
            string.IsNullOrWhiteSpace(result.FinishReason) ? null : result.FinishReason.Trim(),
            string.IsNullOrWhiteSpace(result.ServiceTier) ? null : result.ServiceTier.Trim(),
            retrieval is null
                ? null
                : new ModelCallRetrievalPayload(
                    retrieval.QueryText,
                    retrieval.WasRewritten,
                    Math.Max(0, retrieval.CandidateCount),
                    Math.Max(0, retrieval.SelectedCount),
                    retrieval.RequestedSources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    retrieval.SelectedSources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    retrieval.Citations.Distinct(StringComparer.Ordinal).ToArray()),
            string.IsNullOrWhiteSpace(result.ReasoningSummary) ? null : result.ReasoningSummary.Trim(),
            result.ToolCalls?
                .Where(toolCall =>
                    !string.IsNullOrWhiteSpace(toolCall.Id)
                    && !string.IsNullOrWhiteSpace(toolCall.Type)
                    && !string.IsNullOrWhiteSpace(toolCall.Name))
                .Select(toolCall => new ModelCallToolCallPayload(
                    toolCall.Id.Trim(),
                    toolCall.Type.Trim(),
                    toolCall.Name.Trim(),
                    string.IsNullOrWhiteSpace(toolCall.Arguments) ? null : toolCall.Arguments.Trim()))
                .ToArray()));
    }

    public static bool TryParse(string? payload, out ModelCallPayload? modelCallPayload)
    {
        modelCallPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            modelCallPayload = JsonSerializer.Deserialize<ModelCallPayload>(payload);
            return modelCallPayload is not null
                && string.Equals(modelCallPayload.Type, "model_call", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(modelCallPayload.Model);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
