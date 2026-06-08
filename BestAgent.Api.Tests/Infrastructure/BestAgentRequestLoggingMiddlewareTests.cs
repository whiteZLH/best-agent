using BestAgent.Api.Tests;
using System.Security.Claims;
using BestAgent.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class BestAgentRequestLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldLogCompletedRequest_WithIdentityScope()
    {
        var logger = new ListLogger<BestAgentRequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-1";
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/agent-runs";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("subject_type", "user"),
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim("tenant_id", "tenant-1"),
            new Claim("session_id", "session-1")
        ], "Bearer"));
        var middleware = new BestAgentRequestLoggingMiddleware(
            async httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
                await Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("HTTP POST /agent-runs responded 202", entry.Message, StringComparison.Ordinal);
        Assert.Contains("user:user-1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("trace-1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("tenant-1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("session-1", entry.Message, StringComparison.Ordinal);
    }
}
