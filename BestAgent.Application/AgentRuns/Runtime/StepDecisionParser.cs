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

            if (string.Equals(action, "handoff", StringComparison.OrdinalIgnoreCase))
            {
                var targetAgent = GetString(root, "targetAgent")
                    ?? GetString(root, "target_agent")
                    ?? GetNestedString(root, "handoff", "targetAgent")
                    ?? GetNestedString(root, "handoff", "target_agent");
                if (string.IsNullOrWhiteSpace(targetAgent))
                {
                    throw new InvalidOperationException("Handoff decision did not include a target agent.");
                }

                var handoffInput = GetString(root, "input")
                    ?? GetString(root, "handoffInput")
                    ?? GetString(root, "handoff_input")
                    ?? GetString(root, "task")
                    ?? GetNestedString(root, "handoff", "input")
                    ?? GetNestedString(root, "handoff", "handoffInput")
                    ?? GetNestedString(root, "handoff", "handoff_input")
                    ?? GetNestedString(root, "handoff", "task");
                var handoffMode = GetString(root, "mode")
                    ?? GetString(root, "handoffMode")
                    ?? GetString(root, "handoff_mode")
                    ?? GetNestedString(root, "handoff", "mode")
                    ?? GetNestedString(root, "handoff", "handoffMode")
                    ?? GetNestedString(root, "handoff", "handoff_mode");
                var handoffReason = GetString(root, "reason")
                    ?? GetNestedString(root, "handoff", "reason");
                var handoffConfidence = GetDouble(root, "confidence")
                    ?? GetNestedDouble(root, "handoff", "confidence");
                var contextOverrides = GetString(root, "contextOverrides")
                    ?? GetString(root, "context_overrides")
                    ?? GetNestedString(root, "handoff", "contextOverrides")
                    ?? GetNestedString(root, "handoff", "context_overrides");
                var memoryOverrides = GetString(root, "memoryOverrides")
                    ?? GetString(root, "memory_overrides")
                    ?? GetNestedString(root, "handoff", "memoryOverrides")
                    ?? GetNestedString(root, "handoff", "memory_overrides");
                var toolOverrides = GetString(root, "toolOverrides")
                    ?? GetString(root, "tool_overrides")
                    ?? GetNestedString(root, "handoff", "toolOverrides")
                    ?? GetNestedString(root, "handoff", "tool_overrides");
                var mergeStrategy = GetString(root, "mergeStrategy")
                    ?? GetString(root, "merge_strategy")
                    ?? GetNestedString(root, "handoff", "mergeStrategy")
                    ?? GetNestedString(root, "handoff", "merge_strategy");
                var approvalRequired = GetBoolean(root, "approvalRequired")
                    ?? GetBoolean(root, "approval_required")
                    ?? GetNestedBoolean(root, "handoff", "approvalRequired")
                    ?? GetNestedBoolean(root, "handoff", "approval_required");

                decision = StepDecision.Handoff(
                    targetAgent.Trim(),
                    handoffInput?.Trim(),
                    handoffMode?.Trim(),
                    handoffReason?.Trim(),
                    handoffConfidence,
                    contextOverrides?.Trim(),
                    memoryOverrides?.Trim(),
                    toolOverrides?.Trim(),
                    approvalRequired,
                    mergeStrategy?.Trim());
                return true;
            }

            if (string.Equals(action, "request_human", StringComparison.OrdinalIgnoreCase))
            {
                var comment = GetString(root, "comment")
                    ?? GetString(root, "message")
                    ?? GetString(root, "reason")
                    ?? GetNestedString(root, "human", "comment")
                    ?? GetNestedString(root, "human", "message")
                    ?? GetNestedString(root, "human", "reason");
                decision = StepDecision.RequestHuman(comment?.Trim());
                return true;
            }

            if (string.Equals(action, "fail", StringComparison.OrdinalIgnoreCase))
            {
                var errorCode = GetString(root, "errorCode")
                    ?? GetString(root, "error_code")
                    ?? GetString(root, "code")
                    ?? GetNestedString(root, "error", "errorCode")
                    ?? GetNestedString(root, "error", "error_code")
                    ?? GetNestedString(root, "error", "code");
                var errorMessage = GetString(root, "message")
                    ?? GetString(root, "errorMessage")
                    ?? GetString(root, "error_message")
                    ?? GetString(root, "reason")
                    ?? GetString(root, "terminate_reason")
                    ?? GetNestedString(root, "error", "message")
                    ?? GetNestedString(root, "error", "errorMessage")
                    ?? GetNestedString(root, "error", "error_message")
                    ?? GetNestedString(root, "error", "reason")
                    ?? GetNestedString(root, "error", "terminate_reason");
                if (string.IsNullOrWhiteSpace(errorMessage))
                {
                    throw new InvalidOperationException("Fail decision did not include an error message.");
                }

                decision = StepDecision.Fail(errorCode?.Trim(), errorMessage.Trim());
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

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static double? GetNestedDouble(JsonElement element, string parentName, string propertyName)
    {
        if (!element.TryGetProperty(parentName, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetDouble(parent, propertyName);
    }

    private static bool? GetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static bool? GetNestedBoolean(JsonElement element, string parentName, string propertyName)
    {
        if (!element.TryGetProperty(parentName, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetBoolean(parent, propertyName);
    }
}
