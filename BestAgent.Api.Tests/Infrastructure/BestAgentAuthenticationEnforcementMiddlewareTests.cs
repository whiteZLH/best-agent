using BestAgent.Api.Infrastructure;
using BestAgent.Application.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class BestAgentAuthenticationEnforcementMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldThrowUnauthorized_WhenBearerHeaderIsInvalid()
    {
        await using var provider = BuildProvider(new BestAgentAuthenticationOptions());
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Request.Headers.Authorization = "Bearer invalid-token";
        var nextCalled = false;
        var middleware = new BestAgentAuthenticationEnforcementMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() => middleware.InvokeAsync(context));

        Assert.Equal("Invalid bearer token.", exception.Message);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ShouldAllowAnonymousRequest_WhenAuthorizationHeaderIsMissing()
    {
        await using var provider = BuildProvider(new BestAgentAuthenticationOptions());
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        var nextCalled = false;
        var middleware = new BestAgentAuthenticationEnforcementMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
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
}
