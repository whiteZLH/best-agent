using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BestAgent.Api.Infrastructure;

public sealed class BestAgentAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly BestAgentAuthenticationOptions _authenticationOptions;

    public BestAgentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        BestAgentAuthenticationOptions authenticationOptions)
        : base(options, logger, encoder)
    {
        _authenticationOptions = authenticationOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationHeader)
            || string.IsNullOrWhiteSpace(authorizationHeader.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var header = authorizationHeader.ToString().Trim();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Authorization header must use Bearer scheme."));
        }

        var token = header[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Bearer token is missing."));
        }

        var user = _authenticationOptions.Users.FirstOrDefault(candidate => TokensEqual(candidate.Token, token));
        if (user is not null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.UserName) ? user.UserId : user.UserName),
                new("subject_type", "user")
            };

            foreach (var role in user.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            if (!string.IsNullOrWhiteSpace(user.TenantId))
            {
                claims.Add(new Claim("tenant_id", user.TenantId));
            }

            if (!string.IsNullOrWhiteSpace(user.SessionId))
            {
                claims.Add(new Claim("session_id", user.SessionId));
            }

            return Task.FromResult(AuthenticateResult.Success(CreateTicket(claims)));
        }

        var service = _authenticationOptions.Services.FirstOrDefault(candidate => TokensEqual(candidate.Token, token));
        if (service is not null)
        {
            var claims = new List<Claim>
            {
                new("service_id", service.ServiceId),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(service.DisplayName) ? service.ServiceId : service.DisplayName),
                new("subject_type", "service")
            };

            foreach (var role in service.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            return Task.FromResult(AuthenticateResult.Success(CreateTicket(claims)));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));
    }

    private AuthenticationTicket CreateTicket(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, BestAgentAuthenticationOptions.SchemeName);
        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            BestAgentAuthenticationOptions.SchemeName);
    }

    private static bool TokensEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        var leftBytes = Encoding.UTF8.GetBytes(left.Trim());
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
