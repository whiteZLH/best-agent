using System.Diagnostics;
using System.Security.Claims;

namespace BestAgent.Api.Infrastructure;

public sealed class BestAgentRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BestAgentRequestLoggingMiddleware> _logger;

    public BestAgentRequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<BestAgentRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        await _next(context);

        var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var subjectType = ReadClaim(context.User, "subject_type") ?? "anonymous";
        var subjectId = ReadClaim(context.User, ClaimTypes.NameIdentifier)
            ?? ReadClaim(context.User, "service_id")
            ?? "anonymous";
        var tenantId = ReadClaim(context.User, "tenant_id")
            ?? context.Request.Headers["X-BestAgent-Tenant-Id"].ToString();
        var sessionId = ReadClaim(context.User, "session_id")
            ?? context.Request.Headers["X-BestAgent-Session-Id"].ToString();

        _logger.LogInformation(
            "HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms for {SubjectType}:{SubjectId} request {RequestId} tenant {TenantId} session {SessionId}",
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Response.StatusCode,
            durationMs,
            subjectType,
            subjectId,
            context.TraceIdentifier,
            NormalizeOptional(tenantId),
            NormalizeOptional(sessionId));
    }

    private static string? ReadClaim(ClaimsPrincipal principal, string claimType)
    {
        return principal.FindFirst(claimType)?.Value;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
