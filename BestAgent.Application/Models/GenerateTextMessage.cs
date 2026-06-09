namespace BestAgent.Application.Models;

public record GenerateTextMessage(
    string Role,
    string? Content = null,
    string? Name = null,
    string? ToolCallId = null,
    IReadOnlyList<GenerateTextMessageContentPart>? ContentParts = null,
    IReadOnlyList<GenerateTextToolCall>? ToolCalls = null);
