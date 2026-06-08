using AutoMapper;
using BestAgent.Api.Contracts.Tools;
using BestAgent.Application.Tools;
using BestAgent.Application.Tools.Commands.CreateToolDefinition;
using BestAgent.Application.Tools.Commands.UpdateToolDefinition;
using BestAgent.Application.Tools.Queries.GetToolDefinitionByName;
using BestAgent.Application.Tools.Queries.GetToolDefinitions;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BestAgent.Api.Controllers;

[ApiController]
[Route("tool-definitions")]
public class ToolDefinitionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public ToolDefinitionsController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GetToolDefinitionResponse>>> GetAll(
        [FromQuery] bool? enabledOnly,
        CancellationToken cancellationToken)
    {
        var tools = await _mediator.Send(new GetToolDefinitionsQuery(enabledOnly), cancellationToken);
        return Ok(_mapper.Map<IReadOnlyList<GetToolDefinitionResponse>>(tools));
    }

    [HttpGet("{toolName}")]
    public async Task<ActionResult<GetToolDefinitionResponse>> GetByName(
        [FromRoute] string toolName,
        CancellationToken cancellationToken)
    {
        var tool = await _mediator.Send(new GetToolDefinitionByNameQuery(toolName), cancellationToken);
        if (tool is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<GetToolDefinitionResponse>(tool));
    }

    [HttpPost]
    public async Task<ActionResult<GetToolDefinitionResponse>> Create(
        [FromBody] CreateToolDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var execution = ResolveExecutionRequest(
            request.ExecutionKind,
            request.ExecutionBinding,
            request.EndpointUrl,
            request.HttpMethod,
            request.AuthHeaders,
            request.Execution);
        var policies = ResolvePolicyRequest(
            request.RetryPolicy,
            request.AuthPolicy,
            request.ParameterPolicy,
            request.IdempotencyPolicy,
            request.CompensationPolicy,
            request.Policies);
        var command = new CreateToolDefinitionCommand(
            request.ToolName,
            request.DisplayName,
            request.Description,
            request.InputSchema,
            request.OutputSchema,
            execution.ExecutionKind,
            execution.ExecutionBinding,
            execution.EndpointUrl,
            execution.HttpMethod,
            execution.AuthHeaders,
            request.CallbackSecret,
            request.SideEffectLevel,
            request.TimeoutMs,
            policies.RetryPolicy,
            policies.AuthPolicy,
            policies.IdempotencyPolicy,
            request.AsyncSupported,
            request.ConsistencyMode,
            policies.CompensationPolicy,
            request.Enabled,
            policies.ParameterPolicy);
        var tool = await _mediator.Send(command, cancellationToken);
        var response = _mapper.Map<GetToolDefinitionResponse>(tool);

        return Created($"/tool-definitions/{response.ToolName}", response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<GetToolDefinitionResponse>> Update(
        [FromRoute] string id,
        [FromBody] UpdateToolDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var execution = ResolveExecutionRequest(
            request.ExecutionKind,
            request.ExecutionBinding,
            request.EndpointUrl,
            request.HttpMethod,
            request.AuthHeaders,
            request.Execution);
        var policies = ResolvePolicyRequest(
            request.RetryPolicy,
            request.AuthPolicy,
            request.ParameterPolicy,
            request.IdempotencyPolicy,
            request.CompensationPolicy,
            request.Policies);
        var command = new UpdateToolDefinitionCommand(
            id,
            request.DisplayName,
            request.Description,
            request.InputSchema,
            request.OutputSchema,
            execution.ExecutionKind,
            execution.ExecutionBinding,
            execution.EndpointUrl,
            execution.HttpMethod,
            execution.AuthHeaders,
            request.CallbackSecret,
            request.SideEffectLevel,
            request.TimeoutMs,
            policies.RetryPolicy,
            policies.AuthPolicy,
            policies.IdempotencyPolicy,
            request.AsyncSupported,
            request.ConsistencyMode,
            policies.CompensationPolicy,
            request.Enabled,
            policies.ParameterPolicy);

        var tool = await _mediator.Send(command, cancellationToken);
        return Ok(_mapper.Map<GetToolDefinitionResponse>(tool));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(new Application.Tools.Commands.DeleteToolDefinition.DeleteToolDefinitionCommand(id), cancellationToken);
        return NoContent();
    }

    private static ResolvedToolExecutionRequest ResolveExecutionRequest(
        string? executionKind,
        string? executionBinding,
        string? endpointUrl,
        string? httpMethod,
        string? authHeaders,
        ToolExecutionRequest? execution)
    {
        if (execution is null)
        {
            return new ResolvedToolExecutionRequest(
                executionKind,
                executionBinding,
                endpointUrl,
                httpMethod,
                authHeaders);
        }

        var kind = ToolExecutionBindingHelper.NormalizeExecutionKind(execution.Kind, nameof(execution.Kind));
        var resolved = kind switch
        {
            var value when value == ToolExecutionBindingHelper.Webhook && execution.Webhook is not null
                => ResolveWebhookExecutionRequest(execution, execution.Webhook),
            var value when value == ToolExecutionBindingHelper.LocalHandler && execution.LocalHandler is not null
                => ResolveLocalHandlerExecutionRequest(execution, execution.LocalHandler),
            var value when value == ToolExecutionBindingHelper.InlineResult && execution.InlineResult is not null
                => ResolveInlineResultExecutionRequest(execution, execution.InlineResult),
            _ => throw new InvalidOperationException("Execution payload does not match Execution.Kind.")
        };

        ValidateLegacyExecutionInputConsistency(
            executionKind,
            executionBinding,
            endpointUrl,
            httpMethod,
            authHeaders,
            resolved);

        return resolved;
    }

    private static ResolvedToolExecutionRequest ResolveWebhookExecutionRequest(
        ToolExecutionRequest execution,
        WebhookToolExecutionRequest webhook)
    {
        var normalizedHttpMethod = string.IsNullOrWhiteSpace(webhook.HttpMethod)
            ? "POST"
            : webhook.HttpMethod.Trim().ToUpperInvariant();
        return new ResolvedToolExecutionRequest(
            execution.Kind,
            ToolExecutionBindingHelper.CreateWebhookBinding(
                webhook.EndpointUrl,
                normalizedHttpMethod,
                webhook.AuthHeaders),
            webhook.EndpointUrl,
            normalizedHttpMethod,
            webhook.AuthHeaders);
    }

    private static ResolvedToolExecutionRequest ResolveLocalHandlerExecutionRequest(
        ToolExecutionRequest execution,
        LocalHandlerToolExecutionRequest localHandler)
    {
        return new ResolvedToolExecutionRequest(
            execution.Kind,
            ToolExecutionBindingHelper.CreateLocalHandlerBinding(localHandler.HandlerName),
            null,
            null,
            null);
    }

    private static ResolvedToolExecutionRequest ResolveInlineResultExecutionRequest(
        ToolExecutionRequest execution,
        InlineResultToolExecutionRequest inlineResult)
    {
        return new ResolvedToolExecutionRequest(
            execution.Kind,
            ToolExecutionBindingHelper.CreateInlineResultBinding(inlineResult.Output, inlineResult.Meta),
            null,
            null,
            null);
    }

    private static void ValidateLegacyExecutionInputConsistency(
        string? executionKind,
        string? executionBinding,
        string? endpointUrl,
        string? httpMethod,
        string? authHeaders,
        ResolvedToolExecutionRequest resolved)
    {
        if (!string.IsNullOrWhiteSpace(executionKind))
        {
            var normalizedExecutionKind = ToolExecutionBindingHelper.NormalizeExecutionKind(
                executionKind,
                nameof(executionKind));
            if (!string.Equals(normalizedExecutionKind, resolved.ExecutionKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ExecutionKind must match Execution.Kind when both are provided.");
            }
        }

        if (!string.IsNullOrWhiteSpace(executionBinding))
        {
            ValidateLegacyExecutionBindingConsistency(executionBinding, resolved);
        }

        ValidateLegacyWebhookFieldConsistency(endpointUrl, httpMethod, authHeaders, resolved);
    }

    private static void ValidateLegacyExecutionBindingConsistency(
        string executionBinding,
        ResolvedToolExecutionRequest resolved)
    {
        if (string.Equals(resolved.ExecutionKind, ToolExecutionBindingHelper.Webhook, StringComparison.Ordinal))
        {
            var legacyBinding = ToolExecutionBindingHelper.ParseWebhookBinding(executionBinding, nameof(executionBinding));
            if (!string.Equals(legacyBinding.EndpointUrl, resolved.EndpointUrl, StringComparison.Ordinal)
                || !string.Equals(legacyBinding.HttpMethod, resolved.HttpMethod, StringComparison.Ordinal)
                || !string.Equals(
                    NormalizeOptionalJsonObject(legacyBinding.AuthHeaders),
                    NormalizeOptionalJsonObject(resolved.AuthHeaders),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ExecutionBinding must match Execution when both are provided.");
            }

            return;
        }

        if (string.Equals(resolved.ExecutionKind, ToolExecutionBindingHelper.LocalHandler, StringComparison.Ordinal))
        {
            var legacyBinding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(executionBinding, nameof(executionBinding));
            var resolvedBinding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(
                resolved.ExecutionBinding,
                nameof(resolved.ExecutionBinding));
            if (!string.Equals(legacyBinding.HandlerName, resolvedBinding.HandlerName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ExecutionBinding must match Execution when both are provided.");
            }

            return;
        }

        var inlineLegacyBinding = ToolExecutionBindingHelper.ParseInlineResultBinding(executionBinding, nameof(executionBinding));
        var resolvedInlineBinding = ToolExecutionBindingHelper.ParseInlineResultBinding(
            resolved.ExecutionBinding,
            nameof(resolved.ExecutionBinding));
        if (!string.Equals(inlineLegacyBinding.Output, resolvedInlineBinding.Output, StringComparison.Ordinal)
            || !string.Equals(
                NormalizeOptionalJsonObject(inlineLegacyBinding.Meta),
                NormalizeOptionalJsonObject(resolvedInlineBinding.Meta),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ExecutionBinding must match Execution when both are provided.");
        }
    }

    private static void ValidateLegacyWebhookFieldConsistency(
        string? endpointUrl,
        string? httpMethod,
        string? authHeaders,
        ResolvedToolExecutionRequest resolved)
    {
        if (string.Equals(resolved.ExecutionKind, ToolExecutionBindingHelper.Webhook, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(endpointUrl)
                && !string.Equals(endpointUrl.Trim(), resolved.EndpointUrl, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("EndpointUrl must match Execution when both are provided.");
            }

            if (!string.IsNullOrWhiteSpace(httpMethod))
            {
                var normalizedHttpMethod = httpMethod.Trim().ToUpperInvariant();
                if (!string.Equals(normalizedHttpMethod, resolved.HttpMethod, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("HttpMethod must match Execution when both are provided.");
                }
            }

            if (!string.IsNullOrWhiteSpace(authHeaders)
                && !string.Equals(
                    NormalizeOptionalJsonObject(authHeaders),
                    NormalizeOptionalJsonObject(resolved.AuthHeaders),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("AuthHeaders must match Execution when both are provided.");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(endpointUrl))
        {
            throw new InvalidOperationException($"EndpointUrl must be omitted when Execution.Kind is '{resolved.ExecutionKind}'.");
        }

        if (!string.IsNullOrWhiteSpace(httpMethod))
        {
            throw new InvalidOperationException($"HttpMethod must be omitted when Execution.Kind is '{resolved.ExecutionKind}'.");
        }

        if (!string.IsNullOrWhiteSpace(authHeaders))
        {
            throw new InvalidOperationException($"AuthHeaders must be omitted when Execution.Kind is '{resolved.ExecutionKind}'.");
        }
    }

    private static string? NormalizeOptionalJsonObject(string? value)
    {
        return ToolDefinitionJsonValidator.NormalizeOptionalJsonObject(value, nameof(value));
    }

    private static ResolvedToolPoliciesRequest ResolvePolicyRequest(
        string? retryPolicy,
        string? authPolicy,
        string? parameterPolicy,
        string? idempotencyPolicy,
        string? compensationPolicy,
        ToolPoliciesRequest? policies)
    {
        if (policies is null)
        {
            return new ResolvedToolPoliciesRequest(
                retryPolicy,
                authPolicy,
                parameterPolicy,
                idempotencyPolicy,
                compensationPolicy);
        }

        if (policies.Retry is null
            && policies.Auth is null
            && policies.Parameter is null
            && policies.Idempotency is null
            && policies.Compensation is null)
        {
            throw new InvalidOperationException("Policies payload must include at least one policy.");
        }

        var structuredRetryPolicy = ResolveRetryPolicy(policies.Retry);
        var structuredAuthPolicy = ResolveAuthPolicy(policies.Auth);
        var structuredParameterPolicy = ResolveParameterPolicy(policies.Parameter);
        var structuredIdempotencyPolicy = ResolveIdempotencyPolicy(policies.Idempotency);
        var structuredCompensationPolicy = ResolveCompensationPolicy(policies.Compensation);

        ValidateLegacyPolicyConsistency(
            retryPolicy,
            authPolicy,
            parameterPolicy,
            idempotencyPolicy,
            compensationPolicy,
            structuredRetryPolicy,
            structuredAuthPolicy,
            structuredParameterPolicy,
            structuredIdempotencyPolicy,
            structuredCompensationPolicy);

        return new ResolvedToolPoliciesRequest(
            structuredRetryPolicy ?? retryPolicy,
            structuredAuthPolicy ?? authPolicy,
            structuredParameterPolicy ?? parameterPolicy,
            structuredIdempotencyPolicy ?? idempotencyPolicy,
            structuredCompensationPolicy ?? compensationPolicy);
    }

    private static string? ResolveRetryPolicy(RetryToolPolicyRequest? retry)
    {
        if (retry is null)
        {
            return null;
        }

        if (retry.MaxAttempts is null)
        {
            throw new InvalidOperationException("Policies.Retry.MaxAttempts is required.");
        }

        var delayMs = retry.DelayMs ?? 0;
        return $$"""{"maxAttempts":{{retry.MaxAttempts.Value}},"delayMs":{{delayMs}}}""";
    }

    private static string? ResolveAuthPolicy(AuthToolPolicyRequest? auth)
    {
        if (auth is null)
        {
            return null;
        }

        return ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
            auth.Scheme,
            "Policies.Auth",
            "scheme");
    }

    private static string? ResolveParameterPolicy(ParameterToolPolicyRequest? parameter)
    {
        if (parameter is null)
        {
            return null;
        }

        var payload = new Dictionary<string, IReadOnlyList<string>?>();
        if (parameter.AllowedPaths is not null)
        {
            payload["allowedPaths"] = parameter.AllowedPaths;
        }

        if (parameter.DeniedPaths is not null)
        {
            payload["deniedPaths"] = parameter.DeniedPaths;
        }

        return ToolParameterPolicyHelper.NormalizeOptionalPolicy(
            System.Text.Json.JsonSerializer.Serialize(payload),
            "Policies.Parameter");
    }

    private static string? ResolveIdempotencyPolicy(IdempotencyToolPolicyRequest? idempotency)
    {
        if (idempotency is null)
        {
            return null;
        }

        if (idempotency.Enabled is null)
        {
            throw new InvalidOperationException("Policies.Idempotency.Enabled is required.");
        }

        return $$"""{"enabled":{{idempotency.Enabled.Value.ToString().ToLowerInvariant()}}}""";
    }

    private static string? ResolveCompensationPolicy(CompensationToolPolicyRequest? compensation)
    {
        if (compensation is null)
        {
            return null;
        }

        return ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
            compensation.Mode,
            "Policies.Compensation",
            "mode");
    }

    private static void ValidateLegacyPolicyConsistency(
        string? retryPolicy,
        string? authPolicy,
        string? parameterPolicy,
        string? idempotencyPolicy,
        string? compensationPolicy,
        string? structuredRetryPolicy,
        string? structuredAuthPolicy,
        string? structuredParameterPolicy,
        string? structuredIdempotencyPolicy,
        string? structuredCompensationPolicy)
    {
        if (!string.IsNullOrWhiteSpace(retryPolicy)
            && structuredRetryPolicy is not null)
        {
            var normalizedLegacyRetryPolicy = ToolRetryPolicyHelper.NormalizeOptionalPolicy(
                retryPolicy,
                nameof(retryPolicy));
            if (!string.Equals(normalizedLegacyRetryPolicy, structuredRetryPolicy, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("RetryPolicy must match Policies.Retry when both are provided.");
            }
        }

        if (!string.IsNullOrWhiteSpace(authPolicy)
            && structuredAuthPolicy is not null)
        {
            var normalizedLegacyAuthPolicy = ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
                authPolicy,
                nameof(authPolicy),
                "scheme");
            if (!string.Equals(normalizedLegacyAuthPolicy, structuredAuthPolicy, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("AuthPolicy must match Policies.Auth when both are provided.");
            }
        }

        if (!string.IsNullOrWhiteSpace(parameterPolicy)
            && structuredParameterPolicy is not null)
        {
            var normalizedLegacyParameterPolicy = ToolParameterPolicyHelper.NormalizeOptionalPolicy(
                parameterPolicy,
                nameof(parameterPolicy));
            if (!string.Equals(normalizedLegacyParameterPolicy, structuredParameterPolicy, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ParameterPolicy must match Policies.Parameter when both are provided.");
            }
        }

        if (!string.IsNullOrWhiteSpace(idempotencyPolicy)
            && structuredIdempotencyPolicy is not null)
        {
            var normalizedLegacyIdempotencyPolicy = ToolIdempotencyPolicyHelper.NormalizeOptionalPolicy(
                idempotencyPolicy,
                nameof(idempotencyPolicy));
            if (!string.Equals(normalizedLegacyIdempotencyPolicy, structuredIdempotencyPolicy, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("IdempotencyPolicy must match Policies.Idempotency when both are provided.");
            }
        }

        if (!string.IsNullOrWhiteSpace(compensationPolicy)
            && structuredCompensationPolicy is not null)
        {
            var normalizedLegacyCompensationPolicy = ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
                compensationPolicy,
                nameof(compensationPolicy),
                "mode");
            if (!string.Equals(normalizedLegacyCompensationPolicy, structuredCompensationPolicy, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CompensationPolicy must match Policies.Compensation when both are provided.");
            }
        }
    }

    private sealed record ResolvedToolExecutionRequest(
        string? ExecutionKind,
        string? ExecutionBinding,
        string? EndpointUrl,
        string? HttpMethod,
        string? AuthHeaders);

    private sealed record ResolvedToolPoliciesRequest(
        string? RetryPolicy,
        string? AuthPolicy,
        string? ParameterPolicy,
        string? IdempotencyPolicy,
        string? CompensationPolicy);
}
