namespace BestAgent.Application.Models;

public record GenerateTextMessage(
    string Role,
    string Content,
    string? Name = null,
    string? ToolCallId = null);
