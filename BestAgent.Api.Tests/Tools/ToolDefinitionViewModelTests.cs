using BestAgent.Application.Tools;
using BestAgent.Domain.Tools;
using Xunit;

namespace BestAgent.Api.Tests.Tools;

public class ToolDefinitionViewModelTests
{
    [Fact]
    public void FromEntity_ShouldSynthesizeStructuredWebhookExecution_WhenOnlyLegacyFlatFieldsExist()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var entity = new ToolDefinition
        {
            Id = "tool-legacy",
            ToolName = "weather",
            DisplayName = "Weather",
            Description = "legacy webhook tool",
            ExecutionKind = null,
            ExecutionBinding = null,
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "patch",
            AuthHeaders = "{\"Authorization\":\"Bearer token\"}",
            CallbackSecret = "tool-secret",
            SideEffectLevel = "read_only",
            TimeoutMs = 5000,
            ConsistencyMode = "none",
            Enabled = true,
            CreateTime = now,
            LastModifyTime = now
        };

        var viewModel = ToolDefinitionViewModel.FromEntity(entity);

        Assert.Equal(ToolExecutionBindingHelper.Webhook, viewModel.ExecutionKind);
        Assert.NotNull(viewModel.ExecutionBinding);
        Assert.NotNull(viewModel.Execution);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, viewModel.Execution!.Kind);
        Assert.Equal(ToolExecutionBindingHelper.CurrentBindingVersion, viewModel.Execution.Version);
        Assert.NotNull(viewModel.Execution.Webhook);
        Assert.Equal("https://example.com/tools/weather", viewModel.Execution.Webhook!.EndpointUrl);
        Assert.Equal("PATCH", viewModel.Execution.Webhook.HttpMethod);
        Assert.Equal("{\"Authorization\":\"***\"}", viewModel.Execution.Webhook.AuthHeaders);
        Assert.Equal("https://example.com/tools/weather", viewModel.EndpointUrl);
        Assert.Equal("PATCH", viewModel.HttpMethod);
        Assert.Equal("{\"Authorization\":\"***\"}", viewModel.AuthHeaders);
        Assert.Equal("***", viewModel.CallbackSecret);
    }

    [Fact]
    public void FromEntity_ShouldPreferPersistedWebhookBinding_WhenStructuredExecutionExists()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var entity = new ToolDefinition
        {
            Id = "tool-webhook",
            ToolName = "weather",
            DisplayName = "Weather",
            Description = "normalized webhook tool",
            ExecutionKind = ToolExecutionBindingHelper.Webhook,
            ExecutionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://binding.example.com/tools/weather",
                "PUT",
                "{\"Authorization\":\"Bearer binding-token\"}"),
            EndpointUrl = "https://legacy.example.com/tools/weather",
            HttpMethod = "post",
            AuthHeaders = "{\"Authorization\":\"Bearer legacy-token\"}",
            CallbackSecret = "tool-secret",
            SideEffectLevel = "read_only",
            TimeoutMs = 5000,
            ConsistencyMode = "none",
            Enabled = true,
            CreateTime = now,
            LastModifyTime = now
        };

        var viewModel = ToolDefinitionViewModel.FromEntity(entity);

        Assert.Equal(ToolExecutionBindingHelper.Webhook, viewModel.ExecutionKind);
        Assert.NotNull(viewModel.Execution);
        Assert.Equal(ToolExecutionBindingHelper.CurrentBindingVersion, viewModel.Execution!.Version);
        Assert.NotNull(viewModel.Execution!.Webhook);
        Assert.Equal("https://binding.example.com/tools/weather", viewModel.Execution.Webhook!.EndpointUrl);
        Assert.Equal("PUT", viewModel.Execution.Webhook.HttpMethod);
        Assert.Equal("{\"Authorization\":\"***\"}", viewModel.Execution.Webhook.AuthHeaders);
        Assert.Equal("https://binding.example.com/tools/weather", viewModel.EndpointUrl);
        Assert.Equal("PUT", viewModel.HttpMethod);
        Assert.Equal("{\"Authorization\":\"***\"}", viewModel.AuthHeaders);
    }

    [Fact]
    public void FromEntity_ShouldExposeStructuredPolicies_WhenPoliciesExist()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var entity = new ToolDefinition
        {
            Id = "tool-policy",
            ToolName = "weather",
            DisplayName = "Weather",
            ExecutionKind = ToolExecutionBindingHelper.Webhook,
            ExecutionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://example.com/tools/weather",
                "POST",
                "{\"Authorization\":\"Bearer token\"}"),
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "POST",
            AuthHeaders = "{\"Authorization\":\"Bearer token\"}",
            RetryPolicy = "{\"maxAttempts\":4,\"delayMs\":250}",
            AuthPolicy = "{\"scheme\":\"bearer\"}",
            IdempotencyPolicy = "{\"enabled\":true}",
            CompensationPolicy = "{\"mode\":\"manual\"}",
            SideEffectLevel = "read_only",
            TimeoutMs = 5000,
            ConsistencyMode = "none",
            Enabled = true,
            CreateTime = now,
            LastModifyTime = now
        };

        var viewModel = ToolDefinitionViewModel.FromEntity(entity);

        Assert.NotNull(viewModel.Policies);
        Assert.NotNull(viewModel.Policies!.Retry);
        Assert.Equal(4, viewModel.Policies.Retry!.MaxAttempts);
        Assert.Equal(250, viewModel.Policies.Retry.DelayMs);
        Assert.NotNull(viewModel.Policies.Auth);
        Assert.Equal("bearer", viewModel.Policies.Auth!.Scheme);
        Assert.NotNull(viewModel.Policies.Idempotency);
        Assert.True(viewModel.Policies.Idempotency!.Enabled);
        Assert.NotNull(viewModel.Policies.Compensation);
        Assert.Equal("manual", viewModel.Policies.Compensation!.Mode);
    }

    [Fact]
    public void FromEntity_ShouldNormalizeLegacyFlatPolicies_ForReadModel()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var entity = new ToolDefinition
        {
            Id = "tool-policy-legacy",
            ToolName = "weather",
            DisplayName = "Weather",
            ExecutionKind = ToolExecutionBindingHelper.Webhook,
            ExecutionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://example.com/tools/weather",
                "POST",
                "{\"Authorization\":\"Bearer token\"}"),
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "POST",
            AuthHeaders = "{\"Authorization\":\"Bearer token\"}",
            RetryPolicy = "retry-once",
            AuthPolicy = "bearer",
            IdempotencyPolicy = "disabled",
            CompensationPolicy = "manual",
            SideEffectLevel = "ReadOnly",
            TimeoutMs = 5000,
            ConsistencyMode = "Strong",
            Enabled = true,
            CreateTime = now,
            LastModifyTime = now
        };

        var viewModel = ToolDefinitionViewModel.FromEntity(entity);

        Assert.Equal("{\"maxAttempts\":2,\"delayMs\":0}", viewModel.RetryPolicy);
        Assert.Equal("{\"scheme\":\"bearer\"}", viewModel.AuthPolicy);
        Assert.Equal("non-idempotent", viewModel.IdempotencyPolicy);
        Assert.Equal("{\"mode\":\"manual\"}", viewModel.CompensationPolicy);
        Assert.Equal("read_only", viewModel.SideEffectLevel);
        Assert.Equal("strong", viewModel.ConsistencyMode);
        Assert.NotNull(viewModel.Policies);
        Assert.Equal(2, viewModel.Policies!.Retry!.MaxAttempts);
        Assert.Equal("bearer", viewModel.Policies.Auth!.Scheme);
        Assert.False(viewModel.Policies.Idempotency!.Enabled);
        Assert.Equal("manual", viewModel.Policies.Compensation!.Mode);
    }

    [Fact]
    public void FromEntity_ShouldMaskSensitiveFieldsInsideStructuredAuthPolicy()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var entity = new ToolDefinition
        {
            Id = "tool-policy-sensitive",
            ToolName = "weather",
            DisplayName = "Weather",
            ExecutionKind = ToolExecutionBindingHelper.Webhook,
            ExecutionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://example.com/tools/weather",
                "POST",
                "{\"Authorization\":\"Bearer token\"}"),
            EndpointUrl = "https://example.com/tools/weather",
            HttpMethod = "POST",
            AuthHeaders = "{\"Authorization\":\"Bearer token\"}",
            AuthPolicy = "{\"scheme\":\"bearer\",\"token\":\"super-secret\",\"nested\":{\"apiKey\":\"123\",\"label\":\"ok\"}}",
            SideEffectLevel = "read_only",
            TimeoutMs = 5000,
            ConsistencyMode = "none",
            Enabled = true,
            CreateTime = now,
            LastModifyTime = now
        };

        var viewModel = ToolDefinitionViewModel.FromEntity(entity);

        Assert.Equal("{\"scheme\":\"bearer\",\"token\":\"***\",\"nested\":{\"apiKey\":\"***\",\"label\":\"ok\"}}", viewModel.AuthPolicy);
        Assert.NotNull(viewModel.Policies);
        Assert.NotNull(viewModel.Policies!.Auth);
        Assert.Equal("bearer", viewModel.Policies.Auth!.Scheme);
    }

    [Fact]
    public void FromEntity_ShouldMaskSensitiveFieldsInsideInlineResultExecutionBinding()
    {
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var entity = new ToolDefinition
        {
            Id = "tool-inline-sensitive",
            ToolName = "weather_template",
            DisplayName = "Weather Template",
            ExecutionKind = ToolExecutionBindingHelper.InlineResult,
            ExecutionBinding = ToolExecutionBindingHelper.CreateInlineResultBinding(
                "{\"token\":\"secret\",\"temperature\":26.5}",
                "{\"nested\":{\"apiKey\":\"123\"},\"source\":\"inline\"}"),
            SideEffectLevel = "read_only",
            TimeoutMs = 5000,
            ConsistencyMode = "none",
            Enabled = true,
            CreateTime = now,
            LastModifyTime = now
        };

        var viewModel = ToolDefinitionViewModel.FromEntity(entity);
        var maskedBinding = ToolExecutionBindingHelper.ParseInlineResultBinding(
            viewModel.ExecutionBinding,
            nameof(viewModel.ExecutionBinding));

        Assert.Equal("{\"token\":\"***\",\"temperature\":26.5}", maskedBinding.Output);
        Assert.Equal("{\"nested\":{\"apiKey\":\"***\"},\"source\":\"inline\"}", maskedBinding.Meta);
        Assert.NotNull(viewModel.Execution);
        Assert.NotNull(viewModel.Execution!.InlineResult);
        Assert.Equal(ToolExecutionBindingHelper.CurrentBindingVersion, viewModel.Execution.Version);
        Assert.Equal("{\"token\":\"***\",\"temperature\":26.5}", viewModel.Execution.InlineResult!.Output);
        Assert.Equal("{\"nested\":{\"apiKey\":\"***\"},\"source\":\"inline\"}", viewModel.Execution.InlineResult.Meta);
    }

    [Fact]
    public void FromEntity_ShouldCanonicalizeLegacyLocalHandlerBinding_AndExposeExecutionVersion()
    {
        var now = new DateTime(2026, 6, 9, 0, 0, 0, DateTimeKind.Utc);
        var entity = new ToolDefinition
        {
            Id = "tool-local-legacy",
            ToolName = "echo_context",
            DisplayName = "Echo Context",
            ExecutionKind = ToolExecutionBindingHelper.LocalHandler,
            ExecutionBinding = "{\"handlerName\":\"echo_context\"}",
            SideEffectLevel = "read_only",
            TimeoutMs = 5000,
            ConsistencyMode = "none",
            Enabled = true,
            CreateTime = now,
            LastModifyTime = now
        };

        var viewModel = ToolDefinitionViewModel.FromEntity(entity);
        var binding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(
            viewModel.ExecutionBinding,
            nameof(viewModel.ExecutionBinding));

        Assert.Equal("echo_context", binding.HandlerName);
        Assert.NotNull(viewModel.Execution);
        Assert.Equal(ToolExecutionBindingHelper.CurrentBindingVersion, viewModel.Execution!.Version);
        Assert.Contains("\"version\":1", viewModel.ExecutionBinding);
    }
}
