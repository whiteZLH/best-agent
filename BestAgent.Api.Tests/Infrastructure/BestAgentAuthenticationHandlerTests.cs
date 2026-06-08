using System.Security.Claims;
using BestAgent.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class BestAgentAuthenticationHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_ShouldReturnUserPrincipal_WhenBearerMatchesConfiguredUser()
    {
        await using var provider = BuildProvider(new BestAgentAuthenticationOptions
        {
            Users =
            [
                new BestAgentAuthenticatedUser(
                    "user-token",
                    "user-1",
                    "Alice",
                    ["reviewer"],
                    "tenant-1",
                    "session-1")
            ]
        });
        var context = CreateContext(provider, "Bearer user-token");

        var result = await context.AuthenticateAsync(BestAgentAuthenticationOptions.SchemeName);

        Assert.True(result.Succeeded);
        Assert.Equal("user-1", result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("Alice", result.Principal.FindFirstValue(ClaimTypes.Name));
        Assert.Equal("tenant-1", result.Principal.FindFirstValue("tenant_id"));
        Assert.Equal("session-1", result.Principal.FindFirstValue("session_id"));
        Assert.Equal("reviewer", Assert.Single(result.Principal.FindAll(ClaimTypes.Role)).Value);
        Assert.Equal("user", result.Principal.FindFirstValue("subject_type"));
    }

    [Fact]
    public async Task AuthenticateAsync_ShouldReturnServicePrincipal_WhenBearerMatchesConfiguredService()
    {
        await using var provider = BuildProvider(new BestAgentAuthenticationOptions
        {
            Services =
            [
                new BestAgentAuthenticatedService(
                    "service-token",
                    "runtime-worker",
                    "Runtime Worker",
                    ["service"])
            ]
        });
        var context = CreateContext(provider, "Bearer service-token");

        var result = await context.AuthenticateAsync(BestAgentAuthenticationOptions.SchemeName);

        Assert.True(result.Succeeded);
        Assert.Null(result.Principal!.FindFirst(ClaimTypes.NameIdentifier));
        Assert.Equal("runtime-worker", result.Principal.FindFirstValue("service_id"));
        Assert.Equal("Runtime Worker", result.Principal.FindFirstValue(ClaimTypes.Name));
        Assert.Equal("service", result.Principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal("service", result.Principal.FindFirstValue("subject_type"));
    }

    [Fact]
    public async Task AuthenticateAsync_ShouldFail_WhenBearerTokenIsInvalid()
    {
        await using var provider = BuildProvider(new BestAgentAuthenticationOptions
        {
            Users =
            [
                new BestAgentAuthenticatedUser(
                    "expected-token",
                    "user-1",
                    "Alice",
                    [],
                    null,
                    null)
            ]
        });
        var context = CreateContext(provider, "Bearer wrong-token");

        var result = await context.AuthenticateAsync(BestAgentAuthenticationOptions.SchemeName);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid bearer token.", result.Failure?.Message);
    }

    private static ServiceProvider BuildProvider(BestAgentAuthenticationOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services
            .AddAuthentication(BestAgentAuthenticationOptions.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BestAgentAuthenticationHandler>(
                BestAgentAuthenticationOptions.SchemeName,
                _ => { });
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, string authorizationHeader)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Request.Headers.Authorization = authorizationHeader;
        return context;
    }
}
