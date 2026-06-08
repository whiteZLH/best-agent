using System.Diagnostics;
using BestAgent.Application.Tools;
using BestAgent.Application.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestAgent.Infrastructure.Tools;

public class ToolExecutor : IToolExecutor
{
    private readonly IToolResolver _toolResolver;
    private readonly IHttpToolInvoker _httpToolInvoker;
    private readonly IToolInputValidator _toolInputValidator;
    private readonly IToolOutputValidator _toolOutputValidator;
    private readonly IAgentMetrics _agentMetrics;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(
        IToolResolver toolResolver,
        IHttpToolInvoker httpToolInvoker,
        IToolInputValidator toolInputValidator,
        IToolOutputValidator toolOutputValidator,
        IAgentMetrics? agentMetrics = null,
        ILogger<ToolExecutor>? logger = null)
    {
        _toolResolver = toolResolver;
        _httpToolInvoker = httpToolInvoker;
        _toolInputValidator = toolInputValidator;
        _toolOutputValidator = toolOutputValidator;
        _agentMetrics = agentMetrics ?? NullAgentMetrics.Instance;
        _logger = logger ?? NullLogger<ToolExecutor>.Instance;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string? input,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var resolution = await _toolResolver.ResolveAsync(toolName, input, context, cancellationToken);
        if (resolution.Definition is not null)
        {
            _toolInputValidator.Validate(resolution.Definition, input);
            ToolParameterPolicyHelper.ValidateInput(
                resolution.Definition.ToolName,
                resolution.Definition.ParameterPolicy,
                input);
        }

        var startedAt = DateTime.UtcNow;
        using var activity = AgentTracing.Source.StartActivity(AgentTracing.ToolExecutionActivityName, ActivityKind.Client);
        activity?.SetTag("bestagent.tool", toolName);
        activity?.SetTag("bestagent.run_id", context.RunId);
        activity?.SetTag("bestagent.agent_code", context.AgentCode);
        activity?.SetTag("bestagent.execution_kind", resolution.ExecutionKind.ToString());
        _logger.LogDebug(
            "Executing tool {ToolName} for run {RunId} with binding {ExecutionKind}",
            toolName,
            context.RunId,
            resolution.ExecutionKind);
        try
        {
            var result = await (resolution.ExecutionKind switch
            {
                ToolExecutionKind.Webhook when resolution.WebhookRequest is not null
                    => _httpToolInvoker.InvokeAsync(resolution.WebhookRequest, cancellationToken),
                ToolExecutionKind.LocalHandler when resolution.LocalHandler is not null
                    => resolution.LocalHandler(input, context, cancellationToken),
                ToolExecutionKind.InlineResult when resolution.InlineResultRequest is not null
                    => Task.FromResult(ToolExecutionResult.Completed(
                        resolution.InlineResultRequest.ToolName,
                        resolution.InlineResultRequest.Output,
                        resolution.InlineResultRequest.Meta)),
                _ => throw new InvalidOperationException($"Tool '{toolName}' resolved to an invalid execution binding.")
            });

            if (resolution.Definition is not null && !result.IsPending && !result.IsFailed)
            {
                _toolOutputValidator.Validate(resolution.Definition, result.Output);
            }

            _agentMetrics.RecordToolExecution(
                toolName,
                result.IsPending ? "pending" : result.IsFailed ? "failed" : "completed",
                DateTime.UtcNow - startedAt);
            activity?.SetTag("bestagent.status", result.IsPending ? "pending" : result.IsFailed ? "failed" : "completed");
            if (!string.IsNullOrWhiteSpace(result.WaitToken))
            {
                activity?.SetTag("bestagent.wait_token", result.WaitToken);
            }

            activity?.SetStatus(result.IsFailed ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
            _logger.LogInformation(
                "Tool {ToolName} finished with status {Status} in {DurationMs}ms",
                toolName,
                result.IsPending ? "pending" : result.IsFailed ? "failed" : "completed",
                (DateTime.UtcNow - startedAt).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _agentMetrics.RecordToolExecution(toolName, "failed", DateTime.UtcNow - startedAt);
            activity?.SetTag("bestagent.status", "failed");
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex, "Tool {ToolName} failed for run {RunId}", toolName, context.RunId);
            throw;
        }
    }
}
