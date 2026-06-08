namespace BestAgent.Infrastructure.Model;

public class OpenAiOptions
{
    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public decimal Temperature { get; init; } = 0.2m;

    public int? MaxOutputTokens { get; init; }

    public decimal? TopP { get; init; }

    public decimal? PresencePenalty { get; init; }

    public decimal? FrequencyPenalty { get; init; }

    public int? Seed { get; init; }

    public int TimeoutSeconds { get; init; } = 60;

    public IReadOnlyList<string>? StopSequences { get; init; }

    public bool? ParallelToolCalls { get; init; }

    public decimal PromptTokenPricePerMillion { get; init; }

    public decimal CompletionTokenPricePerMillion { get; init; }
}
