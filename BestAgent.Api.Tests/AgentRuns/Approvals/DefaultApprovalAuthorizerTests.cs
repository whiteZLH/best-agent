using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.Exceptions;
using BestAgent.Api.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Approvals;

public class DefaultApprovalAuthorizerTests
{
    [Fact]
    public void Authorize_ShouldLogAuthorizedApproval()
    {
        var logger = new ListLogger<DefaultApprovalAuthorizer>();
        var authorizer = new DefaultApprovalAuthorizer(
            new ApprovalPolicyOptions
            {
                RoleRequiredSideEffectLevels = ["destructive"],
                AllowedApproverRoles = ["admin"]
            },
            logger);

        authorizer.Authorize(new ApprovalAuthorizationContext(
            "run-1",
            "step-1",
            "delete_user",
            "destructive",
            "user-1",
            "Alice",
            "admin"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("Approval authorized for run run-1 step step-1 tool delete_user", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Authorize_ShouldLogDeniedApproval_WhenRoleIsNotAllowed()
    {
        var logger = new ListLogger<DefaultApprovalAuthorizer>();
        var authorizer = new DefaultApprovalAuthorizer(
            new ApprovalPolicyOptions
            {
                RoleRequiredSideEffectLevels = ["destructive"],
                AllowedApproverRoles = ["admin"]
            },
            logger);

        var exception = Assert.Throws<ForbiddenException>(() => authorizer.Authorize(new ApprovalAuthorizationContext(
            "run-1",
            "step-1",
            "delete_user",
            "destructive",
            "user-1",
            "Alice",
            "viewer")));

        Assert.Contains("requires one of roles", exception.Message, StringComparison.Ordinal);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Approval denied for run run-1 step step-1 tool delete_user", entry.Message, StringComparison.Ordinal);
    }
}
