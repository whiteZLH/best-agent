using BestAgent.Application.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Net.Http.Headers;

namespace BestAgent.Api.Infrastructure;

public sealed class BestAgentAuthenticationEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    public BestAgentAuthenticationEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationHeader)
            || string.IsNullOrWhiteSpace(authorizationHeader.ToString()))
        {
            await _next(context);
            return;
        }

        var result = await context.AuthenticateAsync(BestAgentAuthenticationOptions.SchemeName);
        if (!result.Succeeded || result.Principal is null)
        {
            throw new UnauthorizedException(result.Failure?.Message ?? "Invalid authentication credentials.");
        }

        context.User = result.Principal;
        await _next(context);
    }
}
