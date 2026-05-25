using BestAgent.Domain.Runs;

namespace BestAgent.UnitTests.Domain;

public sealed class AgentRunTests
{
    [Fact]
    public void Run_ShouldTransitionThroughExpectedStates()
    {
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "support-main",
            IdempotencyKey = "idem"
        };

        var now = DateTimeOffset.UtcNow;
        run.MoveToRunning(now);
        run.Complete("""{"text":"ok"}""", now.AddSeconds(1));

        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.True(run.IsTerminal());
        Assert.NotNull(run.OutputPayload);
        Assert.True(run.StatusVersion >= 2);
    }
}
