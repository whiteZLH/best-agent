namespace BestAgent.Infrastructure.Model;

public class OpenAiOptions
{
    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 60;

    public decimal PromptTokenPricePerMillion { get; init; }

    public decimal CompletionTokenPricePerMillion { get; init; }
}
