namespace BestAgent.Application.Models;

public record GenerateTextToolDefinition(
    string Name,
    string? Description = null,
    string? InputSchema = null,
    bool? Strict = null);
