namespace BestAgent.Application.AgentRuns.Runtime;

public interface IStepDecisionParser
{
    StepDecision Parse(string modelOutput);
}
