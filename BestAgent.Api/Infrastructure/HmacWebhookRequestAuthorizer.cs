using System.Security.Cryptography;
using System.Text;
using BestAgent.Application.Exceptions;
using Microsoft.AspNetCore.Http;

namespace BestAgent.Api.Infrastructure;

public sealed class HmacWebhookRequestAuthorizer : IWebhookRequestAuthorizer
{
    private readonly WebhookSecurityOptions _options;

    public HmacWebhookRequestAuthorizer(WebhookSecurityOptions options)
    {
        _options = options;
    }

    public Task AuthorizeToolCallbackAsync(HttpRequest request, string? callbackSecret, CancellationToken cancellationToken)
    {
        var candidateSecrets = BuildToolSecrets(callbackSecret);
        return AuthorizeAsync(request, candidateSecrets, "tool", cancellationToken);
    }

    public Task AuthorizeApprovalCallbackAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var candidateSecrets = BuildApprovalSecrets();
        return AuthorizeAsync(request, candidateSecrets, "approval", cancellationToken);
    }

    private async Task AuthorizeAsync(
        HttpRequest request,
        IReadOnlyList<string> secrets,
        string callbackType,
        CancellationToken cancellationToken)
    {
        if (!_options.RequireSignature)
        {
            return;
        }

        if (secrets.Count == 0)
        {
            throw new UnauthorizedException($"Webhook signature secret is not configured for {callbackType} callbacks.");
        }

        if (!request.Headers.TryGetValue(_options.SignatureHeaderName, out var signatureValues)
            || string.IsNullOrWhiteSpace(signatureValues.ToString()))
        {
            throw new UnauthorizedException($"Missing webhook signature header '{_options.SignatureHeaderName}'.");
        }

        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        var actualSignature = signatureValues.ToString().Trim();
        if (!secrets.Any(secret => FixedTimeEquals(ComputeSignature(body, secret), actualSignature)))
        {
            throw new UnauthorizedException($"Invalid webhook signature for {callbackType} callback.");
        }
    }

    private IReadOnlyList<string> BuildToolSecrets(string? callbackSecret)
    {
        var secrets = new List<string>();
        AddIfPresent(secrets, callbackSecret);
        AddIfPresent(secrets, _options.ToolCallbackSecret);
        return secrets;
    }

    private IReadOnlyList<string> BuildApprovalSecrets()
    {
        var secrets = new List<string>();
        if (_options.ApprovalCallbackSecrets is not null)
        {
            foreach (var secret in _options.ApprovalCallbackSecrets)
            {
                AddIfPresent(secrets, secret);
            }
        }

        AddIfPresent(secrets, _options.ApprovalCallbackSecret);
        return secrets;
    }

    private static void AddIfPresent(ICollection<string> values, string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            values.Add(candidate.Trim());
        }
    }

    private static string ComputeSignature(string body, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes(body);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
