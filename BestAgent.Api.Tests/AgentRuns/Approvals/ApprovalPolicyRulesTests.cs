using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Domain.Tools;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Approvals;

public class ApprovalPolicyRulesTests
{
    [Fact]
    public void RequiresApproval_ShouldUseConfiguredSideEffectLevels()
    {
        var options = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = ["destructive"]
        };
        var tool = new ToolDefinition
        {
            ToolName = "weather",
            SideEffectLevel = "destructive"
        };

        var result = ApprovalPolicyRules.RequiresApproval(tool, options);

        Assert.True(result);
    }

    [Fact]
    public void RequiresApproval_ShouldIncludeDestructive_ByDefault()
    {
        var tool = new ToolDefinition
        {
            ToolName = "delete_user",
            SideEffectLevel = "destructive"
        };

        var result = ApprovalPolicyRules.RequiresApproval(tool);

        Assert.True(result);
    }

    [Fact]
    public void HasAllowedApproverRole_ShouldUseConfiguredRoles()
    {
        var options = new ApprovalPolicyOptions
        {
            AllowedApproverRoles = ["security"],
            RoleRequiredSideEffectLevels = ["destructive"]
        };

        var requiresRole = ApprovalPolicyRules.RequiresApprovalRole("destructive", options);
        var hasRole = ApprovalPolicyRules.HasAllowedApproverRole("viewer,security", options);

        Assert.True(requiresRole);
        Assert.True(hasRole);
    }

    [Fact]
    public void EvaluateApprovalRequirement_ShouldRequireApproval_WhenParameterRuleMatches()
    {
        var options = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = [],
            ParameterApprovalRules =
            [
                new ApprovalParameterRule
                {
                    ToolName = "deploy",
                    InputPath = "environment",
                    ExpectedValue = "production",
                    OverrideSideEffectLevel = "destructive"
                }
            ]
        };
        var tool = new ToolDefinition
        {
            ToolName = "deploy",
            SideEffectLevel = "read_only"
        };

        var result = ApprovalPolicyRules.EvaluateApprovalRequirement(
            tool,
            "{\"environment\":\"production\"}",
            options);

        Assert.True(result.RequiresApproval);
        Assert.Equal("destructive", result.SideEffectLevel);
    }

    [Fact]
    public void EvaluateApprovalRequirement_ShouldNormalizeOverrideSideEffectLevel()
    {
        var options = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = [],
            ParameterApprovalRules =
            [
                new ApprovalParameterRule
                {
                    ToolName = "deploy",
                    InputPath = "environment",
                    ExpectedValue = "production",
                    OverrideSideEffectLevel = "ExternalWrite"
                }
            ]
        };
        var tool = new ToolDefinition
        {
            ToolName = "deploy",
            SideEffectLevel = "read_only"
        };

        var result = ApprovalPolicyRules.EvaluateApprovalRequirement(
            tool,
            "{\"environment\":\"production\"}",
            options);

        Assert.True(result.RequiresApproval);
        Assert.Equal("external_write", result.SideEffectLevel);
    }

    [Fact]
    public void EvaluateApprovalRequirement_ShouldNotRequireApproval_WhenParameterRuleDoesNotMatch()
    {
        var options = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = [],
            ParameterApprovalRules =
            [
                new ApprovalParameterRule
                {
                    ToolName = "deploy",
                    InputPath = "environment",
                    ExpectedValue = "production"
                }
            ]
        };
        var tool = new ToolDefinition
        {
            ToolName = "deploy",
            SideEffectLevel = "read_only"
        };

        var result = ApprovalPolicyRules.EvaluateApprovalRequirement(
            tool,
            "{\"environment\":\"staging\"}",
            options);

        Assert.False(result.RequiresApproval);
        Assert.Equal("read_only", result.SideEffectLevel);
    }

    [Fact]
    public void EvaluateApprovalRequirement_ShouldRequireApproval_WhenParameterPathExistsWithoutExpectedValue()
    {
        var options = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = [],
            ParameterApprovalRules =
            [
                new ApprovalParameterRule
                {
                    ToolName = "mail",
                    InputPath = "attachments.0"
                }
            ]
        };
        var tool = new ToolDefinition
        {
            ToolName = "mail",
            SideEffectLevel = "external_write"
        };

        var result = ApprovalPolicyRules.EvaluateApprovalRequirement(
            tool,
            "{\"attachments\":[\"contract.pdf\"]}",
            options);

        Assert.True(result.RequiresApproval);
        Assert.Equal("external_write", result.SideEffectLevel);
    }

    [Fact]
    public void RequiresApproval_ShouldThrow_WhenConfiguredSideEffectLevelIsInvalid()
    {
        var options = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = ["mutating"]
        };
        var tool = new ToolDefinition
        {
            ToolName = "weather",
            SideEffectLevel = "read_only"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => ApprovalPolicyRules.RequiresApproval(tool, options));

        Assert.Equal("ApprovalRequiredSideEffectLevels[0] must be one of: read_only, internal_write, external_write, destructive.", exception.Message);
    }
}
