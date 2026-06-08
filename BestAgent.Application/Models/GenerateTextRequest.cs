namespace BestAgent.Application.Models;

public record GenerateTextRequest(
    string Model,
    string? SystemPrompt,
    string Input,
    decimal? Temperature = null,
    int? MaxOutputTokens = null,
    decimal? TopP = null,
    decimal? PresencePenalty = null,
    decimal? FrequencyPenalty = null,
    int? TimeoutSeconds = null,
    string? OutputMode = null,
    string? OutputSchema = null,
    IReadOnlyList<GenerateTextToolDefinition>? Tools = null,
    IReadOnlyList<GenerateTextMessage>? Messages = null);
