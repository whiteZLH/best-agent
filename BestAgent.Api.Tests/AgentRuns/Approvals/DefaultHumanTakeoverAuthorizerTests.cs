using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.Exceptions;
using BestAgent.Api.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Approvals;

public class DefaultHumanTakeoverAuthorizerTests
{
    [Fact]
    public void Authorize_ShouldLogAuthorizedHumanTakeover()
    {
        var logger = new ListLogger<DefaultHumanTakeoverAuthorizer>();
        var authorizer = new DefaultHumanTakeoverAuthorizer(
            new HumanTakeoverPolicyOptions
            {
                AllowedHumanOperatorRoles = ["operator", "admin"]
            },
            logger);

        authorizer.Authorize(new HumanTakeoverAuthorizationContext(
            "run-1",
            "user-1",
            "Alice",
            "operator"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("Human takeover authorized for run run-1", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Authorize_ShouldLogDeniedHumanTakeover_WhenRoleIsNotAllowed()
    {
        var logger = new ListLogger<DefaultHumanTakeoverAuthorizer>();
        var authorizer = new DefaultHumanTakeoverAuthorizer(
            new HumanTakeoverPolicyOptions
            {
                AllowedHumanOperatorRoles = ["operator", "admin"]
            },
            logger);

        var exception = Assert.Throws<ForbiddenException>(() => authorizer.Authorize(new HumanTakeoverAuthorizationContext(
            "run-1",
            "user-1",
            "Alice",
            "viewer")));

        Assert.Contains("requires one of roles", exception.Message, StringComparison.Ordinal);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Human takeover denied for run run-1", entry.Message, StringComparison.Ordinal);
    }
}
