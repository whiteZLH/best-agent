using System.Diagnostics;
using System.Diagnostics.Metrics;
using BestAgent.Application.Observability;

namespace BestAgent.Infrastructure.Observability;

public sealed class AgentMetrics : IAgentMetrics
{
    private readonly Meter _meter = new("BestAgent.Runtime", "1.0.0");
    private readonly Counter<long> _runCreatedCounter;
    private readonly Counter<long> _runCompletedCounter;
    private readonly Histogram<double> _runTotalCostHistogram;
    private readonly Counter<long> _toolExecutionCounter;
    private readonly Histogram<double> _toolExecutionDurationMs;
    private readonly Counter<long> _modelCallCounter;
    private readonly Histogram<double> _modelCallDurationMs;
    private readonly Counter<long> _modelTokensCounter;
    private readonly Histogram<double> _modelCostHistogram;
    private readonly Counter<long> _approvalWaitStartedCounter;
    private readonly Histogram<double> _approvalWaitDurationMs;
    private readonly Counter<long> _approvalTimeoutCounter;

    public AgentMetrics()
    {
        _runCreatedCounter = _meter.CreateCounter<long>("bestagent.run.created");
        _runCompletedCounter = _meter.CreateCounter<long>("bestagent.run.completed");
        _runTotalCostHistogram = _meter.CreateHistogram<double>("bestagent.run.total_cost", unit: "usd");
        _toolExecutionCounter = _meter.CreateCounter<long>("bestagent.tool.executions");
        _toolExecutionDurationMs = _meter.CreateHistogram<double>("bestagent.tool.duration.ms", unit: "ms");
        _modelCallCounter = _meter.CreateCounter<long>("bestagent.model.calls");
        _modelCallDurationMs = _meter.CreateHistogram<double>("bestagent.model.duration.ms", unit: "ms");
        _modelTokensCounter = _meter.CreateCounter<long>("bestagent.model.tokens");
        _modelCostHistogram = _meter.CreateHistogram<double>("bestagent.model.cost", unit: "usd");
        _approvalWaitStartedCounter = _meter.CreateCounter<long>("bestagent.approval.waits");
        _approvalWaitDurationMs = _meter.CreateHistogram<double>("bestagent.approval.wait.duration.ms", unit: "ms");
        _approvalTimeoutCounter = _meter.CreateCounter<long>("bestagent.approval.timeouts");
    }

    public void RecordRunCreated(string agentCode, bool isChildRun)
    {
        _runCreatedCounter.Add(1, new TagList
        {
            { "agent", Normalize(agentCode) },
            { "run_kind", isChildRun ? "child" : "root" }
        });
    }

    public void RecordRunCompleted(string agentCode, string status, decimal totalCost)
    {
        var tags = new TagList
        {
            { "agent", Normalize(agentCode) },
            { "status", NormalizeStatus(status) }
        };

        _runCompletedCounter.Add(1, tags);

        if (totalCost > 0m)
        {
            _runTotalCostHistogram.Record((double)totalCost, tags);
        }
    }

    public void RecordToolExecution(string toolName, string status, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "tool", Normalize(toolName) },
            { "status", NormalizeStatus(status) }
        };

        _toolExecutionCounter.Add(1, tags);
        _toolExecutionDurationMs.Record(ClampDuration(duration), tags);
    }

    public void RecordModelCall(
        string model,
        string status,
        TimeSpan duration,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        decimal cost)
    {
        var tags = new TagList
        {
            { "model", Normalize(model) },
            { "status", NormalizeStatus(status) }
        };

        _modelCallCounter.Add(1, tags);
        _modelCallDurationMs.Record(ClampDuration(duration), tags);

        if (promptTokens > 0)
        {
            _modelTokensCounter.Add(promptTokens, new TagList
            {
                { "model", Normalize(model) },
                { "status", NormalizeStatus(status) },
                { "token_type", "prompt" }
            });
        }

        if (completionTokens > 0)
        {
            _modelTokensCounter.Add(completionTokens, new TagList
            {
                { "model", Normalize(model) },
                { "status", NormalizeStatus(status) },
                { "token_type", "completion" }
            });
        }

        if (totalTokens > 0)
        {
            _modelTokensCounter.Add(totalTokens, new TagList
            {
                { "model", Normalize(model) },
                { "status", NormalizeStatus(status) },
                { "token_type", "total" }
            });
        }

        if (cost > 0m)
        {
            _modelCostHistogram.Record((double)cost, tags);
        }
    }

    public void RecordApprovalWaitStarted(string agentCode, string stepType)
    {
        _approvalWaitStartedCounter.Add(1, new TagList
        {
            { "agent", Normalize(agentCode) },
            { "step_type", Normalize(stepType) }
        });
    }

    public void RecordApprovalWaitCompleted(string agentCode, string stepType, string outcome, TimeSpan duration)
    {
        _approvalWaitDurationMs.Record(
            ClampDuration(duration),
            new TagList
            {
                { "agent", Normalize(agentCode) },
                { "step_type", Normalize(stepType) },
                { "outcome", NormalizeStatus(outcome) }
            });
    }

    public void RecordApprovalTimedOut(string agentCode, string stepType, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "agent", Normalize(agentCode) },
            { "step_type", Normalize(stepType) }
        };

        _approvalTimeoutCounter.Add(1, tags);
        _approvalWaitDurationMs.Record(
            ClampDuration(duration),
            new TagList
            {
                { "agent", Normalize(agentCode) },
                { "step_type", Normalize(stepType) },
                { "outcome", "timedout" }
            });
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
    }

    private static string NormalizeStatus(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
    }

    private static double ClampDuration(TimeSpan duration)
    {
        return duration.TotalMilliseconds < 0d ? 0d : duration.TotalMilliseconds;
    }
}
