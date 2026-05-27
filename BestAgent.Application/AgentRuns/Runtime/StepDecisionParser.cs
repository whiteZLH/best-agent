using System.Text.Json;

namespace BestAgent.Application.AgentRuns.Runtime;

public class StepDecisionParser : IStepDecisionParser
{
    public StepDecision Parse(string modelOutput)
    {
        if (string.IsNullOrWhiteSpace(modelOutput))
        {
            throw new InvalidOperationException("Model output was empty.");
        }

        if (TryParseJson(modelOutput, out var decision))
        {
            return decision;
        }

        return StepDecision.Respond(modelOutput.Trim());
    }

    private static bool TryParseJson(string modelOutput, out StepDecision decision)
    {
        foreach (var candidate in GetJsonCandidates(modelOutput))
        {
            if (!TryParseCandidate(candidate, out decision))
            {
                continue;
            }

            return true;
        }

        decision = default!;
        return false;
    }

    private static bool TryParseCandidate(string candidate, out StepDecision decision)
    {
        try
        {
            using var document = JsonDocument.Parse(candidate);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                decision = default!;
                return false;
            }

            var root = document.RootElement;
            var action = GetString(root, "action") ?? GetString(root, "type");
            if (string.IsNullOrWhiteSpace(action))
            {
                decision = default!;
                return false;
            }

            if (string.Equals(action, "respond", StringComparison.OrdinalIgnoreCase))
            {
                var response = GetString(root, "response")
                    ?? GetString(root, "output")
                    ?? GetString(root, "content");
                if (string.IsNullOrWhiteSpace(response))
                {
                    throw new InvalidOperationException("Respond decision did not include a response.");
                }

                decision = StepDecision.Respond(response.Trim());
                return true;
            }

            if (string.Equals(action, "tool_call", StringComparison.OrdinalIgnoreCase))
            {
                var toolName = GetString(root, "toolName")
                    ?? GetString(root, "tool_name")
                    ?? GetNestedString(root, "tool", "name");
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    throw new InvalidOperationException("Tool decision did not include a tool name.");
                }

                var toolInput = GetString(root, "toolInput")
                    ?? GetString(root, "tool_input")
                    ?? GetString(root, "input")
                    ?? GetNestedString(root, "tool", "input");

                decision = StepDecision.ToolCall(toolName.Trim(), toolInput?.Trim());
                return true;
            }
        }
        catch (JsonException)
        {
        }

        decision = default!;
        return false;
    }

    private static IEnumerable<string> GetJsonCandidates(string modelOutput)
    {
        yield return modelOutput.Trim();

        const string fence = "```";
        var fenceStart = modelOutput.IndexOf(fence, StringComparison.Ordinal);
        if (fenceStart < 0)
        {
            yield break;
        }

        var contentStart = modelOutput.IndexOf('\n', fenceStart);
        if (contentStart < 0)
        {
            yield break;
        }

        var fenceEnd = modelOutput.IndexOf(fence, contentStart + 1, StringComparison.Ordinal);
        if (fenceEnd < 0)
        {
            yield break;
        }

        var fencedContent = modelOutput[(contentStart + 1)..fenceEnd].Trim();
        if (!string.IsNullOrWhiteSpace(fencedContent))
        {
            yield return fencedContent;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.GetRawText();
    }

    private static string? GetNestedString(JsonElement element, string parentName, string propertyName)
    {
        if (!element.TryGetProperty(parentName, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(parent, propertyName);
    }
}
