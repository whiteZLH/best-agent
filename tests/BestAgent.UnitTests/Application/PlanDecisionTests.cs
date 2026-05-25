using BestAgent.Application.Planning;

namespace BestAgent.UnitTests.Application;

public sealed class PlanDecisionTests
{
    [Fact]
    public void Parse_ShouldReadRespondDecision()
    {
        const string json = """{"type":"respond","reason":"enough","responseMessage":"done","selectedModel":"gpt-test","toolCalls":[]}""";

        var decision = PlanDecision.Parse(json);

        Assert.Equal(PlanDecisionType.Respond, decision.Type);
        Assert.Equal("done", decision.ResponseMessage);
        Assert.Empty(decision.ToolCalls);
    }

    [Fact]
    public void Parse_ShouldReadToolCallDecision()
    {
        const string json = """{"type":"tool_call","reason":"need tool","responseMessage":null,"selectedModel":"gpt-test","toolCalls":[{"toolName":"echo_context","arguments":{"text":"hello"}}]}""";

        var decision = PlanDecision.Parse(json);

        Assert.Equal(PlanDecisionType.ToolCall, decision.Type);
        Assert.Single(decision.ToolCalls);
        Assert.Equal("echo_context", decision.ToolCalls[0].ToolName);
        Assert.Contains("hello", decision.ToolCalls[0].ArgumentsJson, StringComparison.Ordinal);
    }
}
