namespace BestAgent.Infrastructure.Model;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;
}
