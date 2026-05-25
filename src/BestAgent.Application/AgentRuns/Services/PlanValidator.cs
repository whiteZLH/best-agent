using BestAgent.Application.Planning;

namespace BestAgent.Application.AgentRuns.Services;

public sealed class PlanValidator
{
    public void Validate(PlanDecision decision, bool toolCallAllowed)
    {
        if (decision.Type == PlanDecisionType.Respond)
        {
            if (string.IsNullOrWhiteSpace(decision.ResponseMessage))
            {
                throw new InvalidOperationException("Respond plan must include responseMessage.");
            }

            return;
        }

        if (!toolCallAllowed)
        {
            throw new InvalidOperationException("Tool call is not allowed at this stage.");
        }

        if (decision.ToolCalls.Count != 1)
        {
            throw new InvalidOperationException("MVP supports exactly one tool call.");
        }

        if (string.IsNullOrWhiteSpace(decision.ToolCalls[0].ToolName))
        {
            throw new InvalidOperationException("Tool name is required.");
        }
    }
}
