namespace BestAgent.Infrastructure.Runtime;

public sealed class RunOutboxPublisherOptions
{
    public string? EndpointUrl { get; init; }

    public string? AuthorizationHeader { get; init; }

    public int TimeoutSeconds { get; init; } = 5;
}
