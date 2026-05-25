namespace BestAgent.Application.Models;

public record GenerateTextRequest(
    string Model,
    string? SystemPrompt,
    string Input);
