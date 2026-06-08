namespace BestAgent.Api.Infrastructure;

public sealed class BestAgentAuthenticationOptions
{
    public const string SchemeName = "BestAgentBearer";

    public BestAgentAuthenticatedUser[] Users { get; init; } = [];

    public BestAgentAuthenticatedService[] Services { get; init; } = [];
}

public sealed record BestAgentAuthenticatedUser(
    string Token,
    string UserId,
    string UserName,
    string[] Roles,
    string? TenantId,
    string? SessionId);

public sealed record BestAgentAuthenticatedService(
    string Token,
    string ServiceId,
    string DisplayName,
    string[] Roles);
