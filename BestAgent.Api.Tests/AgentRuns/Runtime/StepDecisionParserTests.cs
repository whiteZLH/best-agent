using BestAgent.Application.AgentRuns.Runtime;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Runtime;

public class StepDecisionParserTests
{
    private readonly StepDecisionParser _parser = new();

    [Fact]
    public void Parse_ShouldReadHandoffRouteDecisionMetadata_FromSnakeCaseJson()
    {
        const string json =
            """
            {
              "action": "handoff",
              "target_agent": "support_agent",
              "input": "please handle refund",
              "mode": "delegate_and_wait",
              "reason": "Route to refund specialist",
              "confidence": 0.91,
              "context_overrides": {"mode":"summary_only"},
              "memory_overrides": {"mode":"read_only"},
              "tool_overrides": {"allowed":["faq_search"]},
              "approval_required": true
            }
            """;

        var decision = _parser.Parse(json);

        Assert.Equal("handoff", decision.Action);
        Assert.Equal("support_agent", decision.TargetAgent);
        Assert.Equal("please handle refund", decision.HandoffInput);
        Assert.Equal("delegate_and_wait", decision.HandoffMode);
        Assert.Equal("Route to refund specialist", decision.HandoffReason);
        Assert.Equal(0.91, decision.HandoffConfidence);
        Assert.Equal("{\"mode\":\"summary_only\"}", decision.HandoffContextOverrides);
        Assert.Equal("{\"mode\":\"read_only\"}", decision.HandoffMemoryOverrides);
        Assert.Equal("{\"allowed\":[\"faq_search\"]}", decision.HandoffToolOverrides);
        Assert.True(decision.HandoffApprovalRequired);
    }

    [Fact]
    public void Parse_ShouldReadNestedHandoffMetadata_FromCamelCaseJson()
    {
        const string json =
            """
            {
              "action": "handoff",
              "handoff": {
                "targetAgent": "support_agent",
                "handoffInput": "please handle refund",
                "handoffMode": "route_only",
                "reason": "Specialized refund agent",
                "confidence": "0.75",
                "contextOverrides": {"mode":"summary_only"},
                "memoryOverrides": {"mode":"read_only"},
                "toolOverrides": {"inherit":false},
                "approvalRequired": "false"
              }
            }
            """;

        var decision = _parser.Parse(json);

        Assert.Equal("support_agent", decision.TargetAgent);
        Assert.Equal("please handle refund", decision.HandoffInput);
        Assert.Equal("route_only", decision.HandoffMode);
        Assert.Equal("Specialized refund agent", decision.HandoffReason);
        Assert.Equal(0.75, decision.HandoffConfidence);
        Assert.Equal("{\"mode\":\"summary_only\"}", decision.HandoffContextOverrides);
        Assert.Equal("{\"mode\":\"read_only\"}", decision.HandoffMemoryOverrides);
        Assert.Equal("{\"inherit\":false}", decision.HandoffToolOverrides);
        Assert.False(decision.HandoffApprovalRequired);
    }

    [Fact]
    public void Parse_ShouldReadRequestHumanDecision()
    {
        const string json =
            """
            {
              "action": "request_human",
              "comment": "Need manual confirmation for refund eligibility."
            }
            """;

        var decision = _parser.Parse(json);

        Assert.Equal("request_human", decision.Action);
        Assert.Equal("Need manual confirmation for refund eligibility.", decision.HumanComment);
    }

    [Fact]
    public void Parse_ShouldReadFailDecision()
    {
        const string json =
            """
            {
              "action": "fail",
              "error_code": "upstream_unavailable",
              "terminate_reason": "The upstream system is unavailable."
            }
            """;

        var decision = _parser.Parse(json);

        Assert.Equal("fail", decision.Action);
        Assert.Equal("upstream_unavailable", decision.FailErrorCode);
        Assert.Equal("The upstream system is unavailable.", decision.FailMessage);
    }
}
