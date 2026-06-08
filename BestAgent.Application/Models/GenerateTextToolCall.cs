namespace BestAgent.Application.Models;

public record GenerateTextToolCall(
    string Id,
    string Type,
    string Name,
    string? Arguments = null);
