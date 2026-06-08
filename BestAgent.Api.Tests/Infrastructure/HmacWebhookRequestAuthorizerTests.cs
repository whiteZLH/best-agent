using System.Security.Cryptography;
using System.Text;
using BestAgent.Api.Infrastructure;
using BestAgent.Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class HmacWebhookRequestAuthorizerTests
{
    [Fact]
    public async Task AuthorizeToolCallbackAsync_ShouldPass_WhenSignatureMatches()
    {
        const string body = """{"waitToken":"wait-1","toolResult":"ok"}""";
        var options = new WebhookSecurityOptions
        {
            RequireSignature = true,
            ToolCallbackSecret = "tool-secret",
            ApprovalCallbackSecret = "approval-secret",
            SignatureHeaderName = "X-BestAgent-Signature"
        };
        var authorizer = new HmacWebhookRequestAuthorizer(options);
        var request = CreateRequest(body, options.SignatureHeaderName, ComputeSignature(body, "tool-secret"));

        await authorizer.AuthorizeToolCallbackAsync(request, null, CancellationToken.None);
    }

    [Fact]
    public async Task AuthorizeToolCallbackAsync_ShouldPass_WhenToolSpecificSecretMatches()
    {
        const string body = """{"waitToken":"wait-1","toolResult":"ok"}""";
        var options = new WebhookSecurityOptions
        {
            RequireSignature = true,
            ToolCallbackSecret = "global-tool-secret",
            ApprovalCallbackSecret = "approval-secret",
            SignatureHeaderName = "X-BestAgent-Signature"
        };
        var authorizer = new HmacWebhookRequestAuthorizer(options);
        var request = CreateRequest(body, options.SignatureHeaderName, ComputeSignature(body, "tool-specific-secret"));

        await authorizer.AuthorizeToolCallbackAsync(request, "tool-specific-secret", CancellationToken.None);
    }

    [Fact]
    public async Task AuthorizeApprovalCallbackAsync_ShouldThrowUnauthorized_WhenSignatureIsMissing()
    {
        const string body = """{"decision":"Approved"}""";
        var options = new WebhookSecurityOptions
        {
            RequireSignature = true,
            ToolCallbackSecret = "tool-secret",
            ApprovalCallbackSecret = "approval-secret",
            SignatureHeaderName = "X-BestAgent-Signature"
        };
        var authorizer = new HmacWebhookRequestAuthorizer(options);
        var request = CreateRequest(body);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            authorizer.AuthorizeApprovalCallbackAsync(request, CancellationToken.None));

        Assert.Contains("Missing webhook signature header", ex.Message);
    }

    [Fact]
    public async Task AuthorizeApprovalCallbackAsync_ShouldThrowUnauthorized_WhenSignatureDoesNotMatch()
    {
        const string body = """{"decision":"Approved"}""";
        var options = new WebhookSecurityOptions
        {
            RequireSignature = true,
            ToolCallbackSecret = "tool-secret",
            ApprovalCallbackSecret = "approval-secret",
            SignatureHeaderName = "X-BestAgent-Signature"
        };
        var authorizer = new HmacWebhookRequestAuthorizer(options);
        var request = CreateRequest(body, options.SignatureHeaderName, "bad-signature");

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            authorizer.AuthorizeApprovalCallbackAsync(request, CancellationToken.None));

        Assert.Contains("Invalid webhook signature", ex.Message);
    }

    [Fact]
    public async Task AuthorizeApprovalCallbackAsync_ShouldPass_WhenRotatedSecretMatches()
    {
        const string body = """{"decision":"Approved"}""";
        var options = new WebhookSecurityOptions
        {
            RequireSignature = true,
            ToolCallbackSecret = "tool-secret",
            ApprovalCallbackSecret = "approval-secret-old",
            ApprovalCallbackSecrets = ["approval-secret-new", "approval-secret-next"],
            SignatureHeaderName = "X-BestAgent-Signature"
        };
        var authorizer = new HmacWebhookRequestAuthorizer(options);
        var request = CreateRequest(body, options.SignatureHeaderName, ComputeSignature(body, "approval-secret-next"));

        await authorizer.AuthorizeApprovalCallbackAsync(request, CancellationToken.None);
    }

    private static HttpRequest CreateRequest(string body, string? headerName = null, string? signature = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Request.ContentType = "application/json";

        if (!string.IsNullOrWhiteSpace(headerName) && !string.IsNullOrWhiteSpace(signature))
        {
            context.Request.Headers[headerName] = signature;
        }

        return context.Request;
    }

    private static string ComputeSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
