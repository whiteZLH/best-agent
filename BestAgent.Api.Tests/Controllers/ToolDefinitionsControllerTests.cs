using AutoMapper;
using BestAgent.Api.Contracts.Tools;
using BestAgent.Api.Controllers;
using BestAgent.Api.Mappings;
using BestAgent.Application.Tools;
using BestAgent.Application.Tools.Commands.CreateToolDefinition;
using BestAgent.Application.Tools.Commands.DeleteToolDefinition;
using BestAgent.Application.Tools.Commands.UpdateToolDefinition;
using BestAgent.Application.Tools.Queries.GetToolDefinitionByName;
using BestAgent.Application.Tools.Queries.GetToolDefinitions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BestAgent.Api.Tests.Controllers;

public class ToolDefinitionsControllerTests
{
    private readonly IMapper _mapper;

    public ToolDefinitionsControllerTests()
    {
        var configuration = new MapperConfiguration(config =>
        {
            config.AddProfile<ApiMappingProfile>();
        }, NullLoggerFactory.Instance);

        _mapper = configuration.CreateMapper();
    }

    [Fact]
    public async Task GetAll_ShouldPassEnabledOnlyQueryAndReturnMappedResponse()
    {
        var mediator = new FakeMediator((GetToolDefinitionsQuery query) =>
        {
            Assert.True(query.EnabledOnly);
            return (IReadOnlyList<ToolDefinitionViewModel>)
            [
                CreateViewModel(toolName: "weather", enabled: true)
            ];
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetAll(true, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetToolDefinitionResponse>>(okResult.Value);
        var tool = Assert.Single(response);
        Assert.Equal("weather", tool.ToolName);
        Assert.Equal("Weather", tool.DisplayName);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, tool.ExecutionKind);
        Assert.NotNull(tool.ExecutionBinding);
        Assert.NotNull(tool.Execution);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, tool.Execution!.Kind);
        Assert.NotNull(tool.Execution.Webhook);
        Assert.Equal("https://example.com/tools/weather", tool.Execution.Webhook!.EndpointUrl);
        Assert.Equal("https://example.com/tools/weather", tool.EndpointUrl);
        Assert.Equal("POST", tool.HttpMethod);
        Assert.Equal("{\"Authorization\":\"***\"}", tool.AuthHeaders);
        Assert.Equal("***", tool.CallbackSecret);
        Assert.NotNull(tool.Policies);
        Assert.NotNull(tool.Policies!.Retry);
        Assert.Equal(2, tool.Policies.Retry!.MaxAttempts);
        Assert.Equal(0, tool.Policies.Retry.DelayMs);
        Assert.NotNull(tool.Policies.Auth);
        Assert.Equal("bearer", tool.Policies.Auth!.Scheme);
        Assert.NotNull(tool.Policies.Idempotency);
        Assert.True(tool.Policies.Idempotency!.Enabled);
        Assert.NotNull(tool.Policies.Compensation);
        Assert.Equal("none", tool.Policies.Compensation!.Mode);
        Assert.Equal(5000, tool.TimeoutMs);
        Assert.True(tool.Enabled);
    }

    [Fact]
    public async Task GetByName_ShouldReturnNotFound_WhenToolDoesNotExist()
    {
        var mediator = new FakeMediator((GetToolDefinitionByNameQuery _) => (ToolDefinitionViewModel?)null);
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByName("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(actionResult.Result);
    }

    [Fact]
    public async Task GetByName_ShouldReturnMappedResponse_WhenToolExists()
    {
        var mediator = new FakeMediator((GetToolDefinitionByNameQuery query) =>
        {
            Assert.Equal("weather", query.ToolName);
            return CreateViewModel(toolName: "weather", enabled: true);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByName("weather", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.Equal("weather", response.ToolName);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, response.Execution!.Kind);
        Assert.Equal("{\"Authorization\":\"***\"}", response.AuthHeaders);
        Assert.Equal("***", response.CallbackSecret);
        Assert.NotNull(response.Policies);
        Assert.True(response.Policies!.Idempotency!.Enabled);
    }

    [Fact]
    public async Task GetByName_ShouldReturnNormalizedFlatPolicies_WhenViewModelUsesCanonicalPolicies()
    {
        var mediator = new FakeMediator((GetToolDefinitionByNameQuery query) =>
        {
            Assert.Equal("weather", query.ToolName);
            return CreateViewModel(
                toolName: "weather",
                enabled: true,
                retryPolicy: "{\"maxAttempts\":2,\"delayMs\":0}",
                authPolicy: "{\"scheme\":\"bearer\"}",
                idempotencyPolicy: "non-idempotent",
                compensationPolicy: "{\"mode\":\"manual\"}",
                consistencyMode: "strong");
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByName("weather", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.Equal("{\"maxAttempts\":2,\"delayMs\":0}", response.RetryPolicy);
        Assert.Equal("{\"scheme\":\"bearer\"}", response.AuthPolicy);
        Assert.Equal("non-idempotent", response.IdempotencyPolicy);
        Assert.Equal("{\"mode\":\"manual\"}", response.CompensationPolicy);
        Assert.Equal("strong", response.ConsistencyMode);
        Assert.NotNull(response.Policies);
        Assert.False(response.Policies!.Idempotency!.Enabled);
        Assert.Equal("manual", response.Policies.Compensation!.Mode);
    }

    [Fact]
    public async Task GetByName_ShouldReturnMaskedStructuredAuthPolicyPayload()
    {
        var mediator = new FakeMediator((GetToolDefinitionByNameQuery query) =>
        {
            Assert.Equal("weather", query.ToolName);
            return CreateViewModel(
                toolName: "weather",
                enabled: true,
                authPolicy: "{\"scheme\":\"bearer\",\"token\":\"***\",\"nested\":{\"apiKey\":\"***\",\"label\":\"ok\"}}");
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByName("weather", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.Equal("{\"scheme\":\"bearer\",\"token\":\"***\",\"nested\":{\"apiKey\":\"***\",\"label\":\"ok\"}}", response.AuthPolicy);
        Assert.NotNull(response.Policies);
        Assert.Equal("bearer", response.Policies!.Auth!.Scheme);
    }

    [Fact]
    public async Task Create_ShouldMapRequestToCommand_AndReturnCreatedResponse()
    {
        var request = CreateRequest();
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) =>
        {
            Assert.Equal("weather", command.ToolName);
            Assert.Equal("Weather", command.DisplayName);
            Assert.Equal(ToolExecutionBindingHelper.Webhook, command.ExecutionKind);
            var binding = ToolExecutionBindingHelper.ParseWebhookBinding(
                command.ExecutionBinding,
                nameof(command.ExecutionBinding));
            Assert.Equal("https://example.com/tools/weather", binding.EndpointUrl);
            Assert.Equal("POST", binding.HttpMethod);
            Assert.Equal("{\"Authorization\":\"Bearer token\"}", binding.AuthHeaders);
            Assert.Equal("https://example.com/tools/weather", command.EndpointUrl);
            Assert.Equal("POST", command.HttpMethod);
            Assert.Equal("{\"Authorization\":\"Bearer token\"}", command.AuthHeaders);
            Assert.Equal(5000, command.TimeoutMs);
            Assert.True(command.AsyncSupported);
            Assert.True(command.Enabled);
            return CreateViewModel(toolName: command.ToolName, enabled: command.Enabled);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal("/tool-definitions/weather", createdResult.Location);
        var response = Assert.IsType<GetToolDefinitionResponse>(createdResult.Value);
        Assert.Equal("weather", response.ToolName);
        Assert.Equal("Weather", response.DisplayName);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, response.Execution!.Kind);
        Assert.Equal("{\"Authorization\":\"***\"}", response.AuthHeaders);
        Assert.Equal("***", response.CallbackSecret);
    }

    [Fact]
    public async Task Create_ShouldMapStructuredWebhookExecutionToCommand()
    {
        var request = new CreateToolDefinitionRequest(
            "weather",
            "Weather",
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            null,
            null,
            null,
            null,
            null,
            "tool-callback-secret",
            "ReadOnly",
            5000,
            "retry-once",
            "bearer",
            "idempotent",
            true,
            "Strong",
            "none",
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.Webhook,
                new WebhookToolExecutionRequest(
                    "https://example.com/tools/weather",
                    "POST",
                    "{\"Authorization\":\"Bearer token\"}"),
                null,
                null));
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) =>
        {
            Assert.Equal(ToolExecutionBindingHelper.Webhook, command.ExecutionKind);
            var binding = ToolExecutionBindingHelper.ParseWebhookBinding(
                command.ExecutionBinding,
                nameof(command.ExecutionBinding));
            Assert.Equal("https://example.com/tools/weather", binding.EndpointUrl);
            Assert.Equal("POST", binding.HttpMethod);
            Assert.Equal("{\"Authorization\":\"Bearer token\"}", binding.AuthHeaders);
            Assert.Equal("https://example.com/tools/weather", command.EndpointUrl);
            Assert.Equal("POST", command.HttpMethod);
            Assert.Equal("{\"Authorization\":\"Bearer token\"}", command.AuthHeaders);
            return CreateViewModel(toolName: command.ToolName, enabled: command.Enabled);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(createdResult.Value);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, response.Execution!.Kind);
    }

    [Fact]
    public async Task Create_ShouldMapStructuredPoliciesToCommand()
    {
        var request = new CreateToolDefinitionRequest(
            "weather",
            "Weather",
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            null,
            null,
            null,
            null,
            null,
            "tool-callback-secret",
            "ReadOnly",
            5000,
            null,
            null,
            null,
            true,
            "Strong",
            null,
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.Webhook,
                new WebhookToolExecutionRequest(
                    "https://example.com/tools/weather",
                    "POST",
                    "{\"Authorization\":\"Bearer token\"}"),
                null,
                null),
            new ToolPoliciesRequest(
                new RetryToolPolicyRequest(4, 250),
                new AuthToolPolicyRequest("bearer"),
                new IdempotencyToolPolicyRequest(true),
                new CompensationToolPolicyRequest("manual")));
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) =>
        {
            Assert.Equal("{\"maxAttempts\":4,\"delayMs\":250}", command.RetryPolicy);
            Assert.Equal("{\"scheme\":\"bearer\"}", command.AuthPolicy);
            Assert.Equal("{\"enabled\":true}", command.IdempotencyPolicy);
            Assert.Equal("{\"mode\":\"manual\"}", command.CompensationPolicy);
            return CreateViewModel(toolName: command.ToolName, enabled: command.Enabled);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(createdResult.Value);
        Assert.NotNull(response.Policies);
    }

    [Fact]
    public async Task Create_ShouldMapStructuredParameterPolicyToCommand()
    {
        var request = new CreateToolDefinitionRequest(
            "weather",
            "Weather",
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            null,
            null,
            null,
            null,
            null,
            "tool-callback-secret",
            "ReadOnly",
            5000,
            null,
            null,
            null,
            true,
            "Strong",
            null,
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.Webhook,
                new WebhookToolExecutionRequest(
                    "https://example.com/tools/weather",
                    "POST",
                    "{\"Authorization\":\"Bearer token\"}"),
                null,
                null),
            new ToolPoliciesRequest(
                null,
                null,
                null,
                null,
                new ParameterToolPolicyRequest(["city", "unit"], ["debug"])));
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) =>
        {
            Assert.Equal("{\"allowedPaths\":[\"city\",\"unit\"],\"deniedPaths\":[\"debug\"]}", command.ParameterPolicy);
            return CreateViewModel(toolName: command.ToolName, enabled: command.Enabled, parameterPolicy: command.ParameterPolicy);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(createdResult.Value);
        Assert.NotNull(response.Policies);
        Assert.NotNull(response.Policies!.Parameter);
        Assert.Equal(["city", "unit"], response.Policies.Parameter!.AllowedPaths);
        Assert.Equal(["debug"], response.Policies.Parameter.DeniedPaths);
    }

    [Fact]
    public async Task Create_ShouldThrow_WhenStructuredPoliciesConflictWithLegacyFields()
    {
        var request = new CreateToolDefinitionRequest(
            "weather",
            "Weather",
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            null,
            null,
            null,
            null,
            null,
            "tool-callback-secret",
            "ReadOnly",
            5000,
            "retry-once",
            null,
            null,
            true,
            "Strong",
            null,
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.Webhook,
                new WebhookToolExecutionRequest(
                    "https://example.com/tools/weather",
                    "POST",
                    "{\"Authorization\":\"Bearer token\"}"),
                null,
                null),
            new ToolPoliciesRequest(
                new RetryToolPolicyRequest(4, 250),
                null,
                null,
                null));
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) => CreateViewModel(toolName: command.ToolName));
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Create(request, CancellationToken.None));

        Assert.Equal("RetryPolicy must match Policies.Retry when both are provided.", exception.Message);
    }

    [Fact]
    public async Task Create_ShouldThrow_WhenStructuredParameterPolicyConflictsWithLegacyField()
    {
        var request = new CreateToolDefinitionRequest(
            "weather",
            "Weather",
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            null,
            null,
            null,
            null,
            null,
            "tool-callback-secret",
            "ReadOnly",
            5000,
            null,
            null,
            null,
            true,
            "Strong",
            null,
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.Webhook,
                new WebhookToolExecutionRequest(
                    "https://example.com/tools/weather",
                    "POST",
                    "{\"Authorization\":\"Bearer token\"}"),
                null,
                null),
            new ToolPoliciesRequest(
                null,
                null,
                null,
                null,
                new ParameterToolPolicyRequest(["city"], [])),
            "{\"allowedPaths\":[\"unit\"],\"deniedPaths\":[]}");
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) => CreateViewModel(toolName: command.ToolName));
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Create(request, CancellationToken.None));

        Assert.Equal("ParameterPolicy must match Policies.Parameter when both are provided.", exception.Message);
    }

    [Fact]
    public async Task Create_ShouldAllowStructuredWebhookExecution_WhenLegacyFieldsMatch()
    {
        var request = new CreateToolDefinitionRequest(
            "weather",
            "Weather",
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            ToolExecutionBindingHelper.Webhook,
            ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://example.com/tools/weather",
                "POST",
                "{\"Authorization\":\"Bearer token\"}"),
            "https://example.com/tools/weather",
            "POST",
            "{\"Authorization\":\"Bearer token\"}",
            "tool-callback-secret",
            "ReadOnly",
            5000,
            "retry-once",
            "bearer",
            "idempotent",
            true,
            "Strong",
            "none",
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.Webhook,
                new WebhookToolExecutionRequest(
                    "https://example.com/tools/weather",
                    "POST",
                    "{\"Authorization\":\"Bearer token\"}"),
                null,
                null));
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) =>
        {
            Assert.Equal(ToolExecutionBindingHelper.Webhook, command.ExecutionKind);
            Assert.Equal("https://example.com/tools/weather", command.EndpointUrl);
            Assert.Equal("POST", command.HttpMethod);
            Assert.Equal("{\"Authorization\":\"Bearer token\"}", command.AuthHeaders);
            return CreateViewModel(toolName: command.ToolName, enabled: command.Enabled);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(createdResult.Value);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, response.Execution!.Kind);
    }

    [Fact]
    public async Task Create_ShouldThrow_WhenStructuredWebhookExecutionConflictsWithLegacyFields()
    {
        var request = new CreateToolDefinitionRequest(
            "weather",
            "Weather",
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            null,
            null,
            "https://legacy.example.com/tools/weather",
            null,
            null,
            "tool-callback-secret",
            "ReadOnly",
            5000,
            "retry-once",
            "bearer",
            "idempotent",
            true,
            "Strong",
            "none",
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.Webhook,
                new WebhookToolExecutionRequest(
                    "https://example.com/tools/weather",
                    "POST",
                    null),
                null,
                null));
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) => CreateViewModel(toolName: command.ToolName));
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Create(request, CancellationToken.None));

        Assert.Equal("EndpointUrl must match Execution when both are provided.", exception.Message);
    }

    [Fact]
    public async Task Create_ShouldMapStructuredLocalHandlerExecutionToCommand()
    {
        var request = new CreateToolDefinitionRequest(
            "echo_context",
            "Echo Context",
            "debug tool",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "read_only",
            5000,
            null,
            null,
            null,
            false,
            "none",
            null,
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.LocalHandler,
                null,
                new LocalHandlerToolExecutionRequest("echo_context"),
                null));
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) =>
        {
            Assert.Equal(ToolExecutionBindingHelper.LocalHandler, command.ExecutionKind);
            var binding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(
                command.ExecutionBinding,
                nameof(command.ExecutionBinding));
            Assert.Equal("echo_context", binding.HandlerName);
            Assert.Null(command.EndpointUrl);
            Assert.Null(command.HttpMethod);
            Assert.Null(command.AuthHeaders);
            return CreateViewModel(
                toolName: command.ToolName,
                displayName: command.DisplayName,
                executionKind: command.ExecutionKind,
                executionBinding: command.ExecutionBinding,
                endpointUrl: null,
                authHeaders: null,
                asyncSupported: command.AsyncSupported,
                enabled: command.Enabled);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(createdResult.Value);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.LocalHandler, response.Execution!.Kind);
        Assert.NotNull(response.Execution.LocalHandler);
        Assert.Equal("echo_context", response.Execution.LocalHandler!.HandlerName);
    }

    [Fact]
    public async Task Create_ShouldThrow_WhenStructuredLocalHandlerExecutionIncludesLegacyWebhookFields()
    {
        var request = new CreateToolDefinitionRequest(
            "echo_context",
            "Echo Context",
            "debug tool",
            null,
            null,
            null,
            null,
            "https://example.com/tools/echo",
            null,
            null,
            null,
            "read_only",
            5000,
            null,
            null,
            null,
            false,
            "none",
            null,
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.LocalHandler,
                null,
                new LocalHandlerToolExecutionRequest("echo_context"),
                null));
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) => CreateViewModel(toolName: command.ToolName));
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Create(request, CancellationToken.None));

        Assert.Equal("EndpointUrl must be omitted when Execution.Kind is 'local_handler'.", exception.Message);
    }

    [Fact]
    public async Task Update_ShouldMapRouteAndRequestToCommand_AndReturnOkResponse()
    {
        var request = new UpdateToolDefinitionRequest(
            "Weather v2",
            "updated description",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            ToolExecutionBindingHelper.Webhook,
            "{\"endpointUrl\":\"https://example.com/tools/weather-v2\",\"httpMethod\":\"PUT\",\"authHeaders\":\"{\\\"Authorization\\\":\\\"Bearer next-token\\\"}\"}",
            "https://example.com/tools/weather-v2",
            "PUT",
            "{\"Authorization\":\"Bearer next-token\"}",
            "tool-callback-secret-v2",
            "ReadOnly",
            8000,
            "retry-once",
            "bearer",
            "idempotent",
            false,
            "Strong",
            "none",
            false);
        var mediator = new FakeMediator((UpdateToolDefinitionCommand command) =>
        {
            Assert.Equal("tool-001", command.Id);
            Assert.Equal("Weather v2", command.DisplayName);
            Assert.Equal(ToolExecutionBindingHelper.Webhook, command.ExecutionKind);
            Assert.Equal("https://example.com/tools/weather-v2", command.EndpointUrl);
            Assert.Equal("PUT", command.HttpMethod);
            Assert.Equal("{\"Authorization\":\"Bearer next-token\"}", command.AuthHeaders);
            Assert.Equal("tool-callback-secret-v2", command.CallbackSecret);
            Assert.Equal(8000, command.TimeoutMs);
            Assert.False(command.AsyncSupported);
            Assert.False(command.Enabled);
            return CreateViewModel(
                id: command.Id,
                toolName: "weather",
                displayName: command.DisplayName,
                executionKind: command.ExecutionKind,
                executionBinding: command.ExecutionBinding,
                endpointUrl: command.EndpointUrl,
                httpMethod: command.HttpMethod ?? "POST",
                authHeaders: command.AuthHeaders,
                callbackSecret: command.CallbackSecret,
                timeoutMs: command.TimeoutMs,
                asyncSupported: command.AsyncSupported,
                enabled: command.Enabled);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Update("tool-001", request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.Equal("tool-001", response.Id);
        Assert.Equal("Weather v2", response.DisplayName);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, response.ExecutionKind);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.Webhook, response.Execution!.Kind);
        Assert.NotNull(response.Execution.Webhook);
        Assert.Equal("https://example.com/tools/weather-v2", response.Execution.Webhook!.EndpointUrl);
        Assert.Equal("https://example.com/tools/weather-v2", response.EndpointUrl);
        Assert.Equal("PUT", response.HttpMethod);
        Assert.Equal("{\"Authorization\":\"***\"}", response.AuthHeaders);
        Assert.Equal("***", response.CallbackSecret);
        Assert.Equal(8000, response.TimeoutMs);
        Assert.False(response.AsyncSupported);
        Assert.False(response.Enabled);
    }

    [Fact]
    public async Task Update_ShouldMapStructuredInlineResultExecutionToCommand()
    {
        var request = new UpdateToolDefinitionRequest(
            "Weather Template",
            "fixed weather response",
            "{\"type\":\"object\"}",
            "{\"type\":\"object\"}",
            null,
            null,
            null,
            null,
            null,
            null,
            "read_only",
            5000,
            null,
            null,
            null,
            false,
            "none",
            null,
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.InlineResult,
                null,
                null,
                new InlineResultToolExecutionRequest(
                    "{\"temperature\":26.5}",
                    "{\"source\":\"inline\"}")));
        var mediator = new FakeMediator((UpdateToolDefinitionCommand command) =>
        {
            Assert.Equal("tool-001", command.Id);
            Assert.Equal(ToolExecutionBindingHelper.InlineResult, command.ExecutionKind);
            var binding = ToolExecutionBindingHelper.ParseInlineResultBinding(
                command.ExecutionBinding,
                nameof(command.ExecutionBinding));
            Assert.Equal("{\"temperature\":26.5}", binding.Output);
            Assert.Equal("{\"source\":\"inline\"}", binding.Meta);
            Assert.Null(command.EndpointUrl);
            Assert.Null(command.HttpMethod);
            Assert.Null(command.AuthHeaders);
            return CreateViewModel(
                id: command.Id,
                toolName: "weather_template",
                displayName: command.DisplayName,
                executionKind: command.ExecutionKind,
                executionBinding: command.ExecutionBinding,
                endpointUrl: null,
                authHeaders: null,
                timeoutMs: command.TimeoutMs,
                asyncSupported: command.AsyncSupported,
                enabled: command.Enabled);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Update("tool-001", request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.InlineResult, response.Execution!.Kind);
        Assert.NotNull(response.Execution.InlineResult);
        Assert.Equal("{\"temperature\":26.5}", response.Execution.InlineResult!.Output);
    }

    [Fact]
    public async Task Update_ShouldMapStructuredPoliciesToCommand()
    {
        var request = new UpdateToolDefinitionRequest(
            "Weather v2",
            "updated description",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            ToolExecutionBindingHelper.Webhook,
            ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://example.com/tools/weather-v2",
                "PUT",
                "{\"Authorization\":\"Bearer next-token\"}"),
            "https://example.com/tools/weather-v2",
            "PUT",
            "{\"Authorization\":\"Bearer next-token\"}",
            "tool-callback-secret-v2",
            "ReadOnly",
            8000,
            null,
            null,
            null,
            false,
            "Strong",
            null,
            false,
            null,
            new ToolPoliciesRequest(
                new RetryToolPolicyRequest(5, 100),
                new AuthToolPolicyRequest("oauth"),
                new IdempotencyToolPolicyRequest(false),
                new CompensationToolPolicyRequest("manual")));
        var mediator = new FakeMediator((UpdateToolDefinitionCommand command) =>
        {
            Assert.Equal("{\"maxAttempts\":5,\"delayMs\":100}", command.RetryPolicy);
            Assert.Equal("{\"scheme\":\"oauth\"}", command.AuthPolicy);
            Assert.Equal("{\"enabled\":false}", command.IdempotencyPolicy);
            Assert.Equal("{\"mode\":\"manual\"}", command.CompensationPolicy);
            return CreateViewModel(id: command.Id, toolName: "weather", displayName: command.DisplayName);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Update("tool-001", request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.NotNull(response.Policies);
    }

    [Fact]
    public async Task Update_ShouldThrow_WhenStructuredInlineExecutionConflictsWithLegacyBinding()
    {
        var request = new UpdateToolDefinitionRequest(
            "Weather Template",
            "fixed weather response",
            "{\"type\":\"object\"}",
            "{\"type\":\"object\"}",
            null,
            ToolExecutionBindingHelper.CreateInlineResultBinding("{\"temperature\":18.0}"),
            null,
            null,
            null,
            null,
            "read_only",
            5000,
            null,
            null,
            null,
            false,
            "none",
            null,
            true,
            new ToolExecutionRequest(
                ToolExecutionBindingHelper.InlineResult,
                null,
                null,
                new InlineResultToolExecutionRequest(
                    "{\"temperature\":26.5}",
                    "{\"source\":\"inline\"}")));
        var mediator = new FakeMediator((UpdateToolDefinitionCommand command) => CreateViewModel(id: command.Id, toolName: "weather_template"));
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Update("tool-001", request, CancellationToken.None));

        Assert.Equal("ExecutionBinding must match Execution when both are provided.", exception.Message);
    }

    [Fact]
    public async Task GetByName_ShouldReturnStructuredExecutionForLocalHandler()
    {
        var mediator = new FakeMediator((GetToolDefinitionByNameQuery query) =>
        {
            Assert.Equal("echo_context", query.ToolName);
            return CreateViewModel(
                toolName: "echo_context",
                displayName: "Echo Context",
                executionKind: ToolExecutionBindingHelper.LocalHandler,
                executionBinding: ToolExecutionBindingHelper.CreateLocalHandlerBinding("echo_context"),
                endpointUrl: null,
                authHeaders: null);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByName("echo_context", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.LocalHandler, response.Execution!.Kind);
        Assert.NotNull(response.Execution.LocalHandler);
        Assert.Equal("echo_context", response.Execution.LocalHandler!.HandlerName);
        Assert.Null(response.Execution.Webhook);
        Assert.Null(response.Execution.InlineResult);
        Assert.Null(response.EndpointUrl);
        Assert.Null(response.HttpMethod);
        Assert.Null(response.AuthHeaders);
    }

    [Fact]
    public async Task GetByName_ShouldReturnStructuredExecutionForInlineResult()
    {
        var mediator = new FakeMediator((GetToolDefinitionByNameQuery query) =>
        {
            Assert.Equal("weather_template", query.ToolName);
            return CreateViewModel(
                toolName: "weather_template",
                displayName: "Weather Template",
                executionKind: ToolExecutionBindingHelper.InlineResult,
                executionBinding: ToolExecutionBindingHelper.CreateInlineResultBinding(
                    "{\"temperature\":26.5}",
                    "{\"source\":\"inline\"}"),
                endpointUrl: null,
                authHeaders: null);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByName("weather_template", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.NotNull(response.Execution);
        Assert.Equal(ToolExecutionBindingHelper.InlineResult, response.Execution!.Kind);
        Assert.NotNull(response.Execution.InlineResult);
        Assert.Equal("{\"temperature\":26.5}", response.Execution.InlineResult!.Output);
        Assert.Equal("{\"source\":\"inline\"}", response.Execution.InlineResult.Meta);
        Assert.Null(response.Execution.Webhook);
        Assert.Null(response.Execution.LocalHandler);
        Assert.Null(response.EndpointUrl);
        Assert.Null(response.HttpMethod);
        Assert.Null(response.AuthHeaders);
        Assert.NotNull(response.Policies);
        Assert.Equal("none", response.Policies!.Compensation!.Mode);
    }

    [Fact]
    public async Task GetByName_ShouldReturnMaskedInlineResultExecutionBinding()
    {
        var mediator = new FakeMediator((GetToolDefinitionByNameQuery query) =>
        {
            Assert.Equal("weather_template", query.ToolName);
            return CreateViewModel(
                toolName: "weather_template",
                displayName: "Weather Template",
                executionKind: ToolExecutionBindingHelper.InlineResult,
                executionBinding: ToolExecutionBindingHelper.CreateInlineResultBinding(
                    "{\"token\":\"***\",\"temperature\":26.5}",
                    "{\"nested\":{\"apiKey\":\"***\"},\"source\":\"inline\"}"),
                endpointUrl: null,
                authHeaders: null);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByName("weather_template", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        var binding = ToolExecutionBindingHelper.ParseInlineResultBinding(
            response.ExecutionBinding,
            nameof(response.ExecutionBinding));
        Assert.Equal("{\"token\":\"***\",\"temperature\":26.5}", binding.Output);
        Assert.Equal("{\"nested\":{\"apiKey\":\"***\"},\"source\":\"inline\"}", binding.Meta);
        Assert.NotNull(response.Execution);
        Assert.NotNull(response.Execution!.InlineResult);
        Assert.Equal("{\"token\":\"***\",\"temperature\":26.5}", response.Execution.InlineResult!.Output);
        Assert.Equal("{\"nested\":{\"apiKey\":\"***\"},\"source\":\"inline\"}", response.Execution.InlineResult.Meta);
    }

    [Fact]
    public async Task Delete_ShouldSendDeleteCommand_AndReturnNoContent()
    {
        var deletedIds = new List<string>();
        var mediator = new FakeMediator((DeleteToolDefinitionCommand command) =>
        {
            deletedIds.Add(command.Id);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Delete("tool-001", CancellationToken.None);

        Assert.Equal(["tool-001"], deletedIds);
        Assert.IsType<NoContentResult>(actionResult);
    }

    private static CreateToolDefinitionRequest CreateRequest()
    {
        return new CreateToolDefinitionRequest(
            "weather",
            "Weather",
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            ToolExecutionBindingHelper.Webhook,
            "{\"endpointUrl\":\"https://example.com/tools/weather\",\"httpMethod\":\"POST\",\"authHeaders\":\"{\\\"Authorization\\\":\\\"Bearer token\\\"}\"}",
            "https://example.com/tools/weather",
            "POST",
            "{\"Authorization\":\"Bearer token\"}",
            "tool-callback-secret",
            "ReadOnly",
            5000,
            "retry-once",
            "bearer",
            "idempotent",
            true,
            "Strong",
            "none",
            true);
    }

    private static ToolDefinitionViewModel CreateViewModel(
        string id = "tool-001",
        string toolName = "weather",
        string displayName = "Weather",
        string? executionKind = ToolExecutionBindingHelper.Webhook,
        string? executionBinding = null,
        string? endpointUrl = "https://example.com/tools/weather",
        string? httpMethod = "POST",
        string? authHeaders = "{\"Authorization\":\"Bearer token\"}",
        string? callbackSecret = "tool-callback-secret",
        string? retryPolicy = "retry-once",
        string? authPolicy = "bearer",
        string? parameterPolicy = null,
        string? idempotencyPolicy = "idempotent",
        string? compensationPolicy = "none",
        string consistencyMode = "Strong",
        int timeoutMs = 5000,
        bool asyncSupported = true,
        bool enabled = true)
    {
        var now = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
        var maskedAuthHeaders = ToolSensitiveDataMasker.MaskAuthHeaders(authHeaders);
        var maskedCallbackSecret = ToolSensitiveDataMasker.MaskOptionalSecret(callbackSecret);
        var resolvedHttpMethod = httpMethod ?? "POST";
        var resolvedExecutionBinding = executionBinding ?? ToolExecutionBindingHelper.CreateWebhookBinding(
            endpointUrl ?? "https://example.com/tools/weather",
            resolvedHttpMethod,
            maskedAuthHeaders);
        return new ToolDefinitionViewModel(
            id,
            toolName,
            displayName,
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            executionKind,
            resolvedExecutionBinding,
            CreateExecutionViewModel(executionKind, resolvedExecutionBinding),
            endpointUrl,
            executionKind == ToolExecutionBindingHelper.Webhook ? resolvedHttpMethod : null,
            maskedAuthHeaders,
            maskedCallbackSecret,
            "ReadOnly",
            timeoutMs,
            retryPolicy,
            authPolicy,
            idempotencyPolicy,
            asyncSupported,
            consistencyMode,
            compensationPolicy,
            enabled,
            now,
            now,
            new ToolPoliciesViewModel(
                CreateRetryPolicyViewModel(retryPolicy),
                CreateAuthPolicyViewModel(authPolicy),
                CreateIdempotencyPolicyViewModel(idempotencyPolicy),
                CreateCompensationPolicyViewModel(compensationPolicy),
                CreateParameterPolicyViewModel(parameterPolicy)),
            parameterPolicy);
    }

    private static ToolExecutionViewModel? CreateExecutionViewModel(
        string? executionKind,
        string? executionBinding)
    {
        if (string.IsNullOrWhiteSpace(executionKind) || string.IsNullOrWhiteSpace(executionBinding))
        {
            return null;
        }

        return executionKind switch
        {
            var kind when kind == ToolExecutionBindingHelper.Webhook
                => CreateWebhookExecutionViewModel(executionBinding),
            var kind when kind == ToolExecutionBindingHelper.LocalHandler
                => CreateLocalHandlerExecutionViewModel(executionBinding),
            var kind when kind == ToolExecutionBindingHelper.InlineResult
                => CreateInlineResultExecutionViewModel(executionBinding),
            _ => null
        };
    }

    private static ToolExecutionViewModel CreateWebhookExecutionViewModel(string executionBinding)
    {
        var binding = ToolExecutionBindingHelper.ParseWebhookBinding(executionBinding, nameof(executionBinding));
        return new ToolExecutionViewModel(
            ToolExecutionBindingHelper.Webhook,
            executionBinding,
            new WebhookToolExecutionViewModel(
                binding.EndpointUrl,
                binding.HttpMethod,
                binding.AuthHeaders),
            null,
            null);
    }

    private static ToolExecutionViewModel CreateLocalHandlerExecutionViewModel(string executionBinding)
    {
        var binding = ToolExecutionBindingHelper.ParseLocalHandlerBinding(executionBinding, nameof(executionBinding));
        return new ToolExecutionViewModel(
            ToolExecutionBindingHelper.LocalHandler,
            executionBinding,
            null,
            new LocalHandlerToolExecutionViewModel(binding.HandlerName),
            null);
    }

    private static ToolExecutionViewModel CreateInlineResultExecutionViewModel(string executionBinding)
    {
        var binding = ToolExecutionBindingHelper.ParseInlineResultBinding(executionBinding, nameof(executionBinding));
        return new ToolExecutionViewModel(
            ToolExecutionBindingHelper.InlineResult,
            executionBinding,
            null,
            null,
            new InlineResultToolExecutionViewModel(binding.Output, binding.Meta));
    }

    private static RetryToolPolicyViewModel? CreateRetryPolicyViewModel(string? retryPolicy)
    {
        if (string.IsNullOrWhiteSpace(retryPolicy))
        {
            return null;
        }

        var normalized = ToolRetryPolicyHelper.NormalizeOptionalPolicy(retryPolicy, nameof(retryPolicy));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        using var document = System.Text.Json.JsonDocument.Parse(normalized);
        var root = document.RootElement;
        int? maxAttempts = root.TryGetProperty("maxAttempts", out var maxAttemptsProperty)
            && maxAttemptsProperty.TryGetInt32(out var maxAttemptsValue)
                ? maxAttemptsValue
                : null;
        int? delayMs = root.TryGetProperty("delayMs", out var delayMsProperty)
            && delayMsProperty.TryGetInt32(out var delayMsValue)
                ? delayMsValue
                : null;
        return new RetryToolPolicyViewModel(maxAttempts, delayMs);
    }

    private static AuthToolPolicyViewModel? CreateAuthPolicyViewModel(string? authPolicy)
    {
        if (string.IsNullOrWhiteSpace(authPolicy))
        {
            return null;
        }

        var normalized = ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
            authPolicy,
            nameof(authPolicy),
            "scheme");
        using var document = System.Text.Json.JsonDocument.Parse(normalized!);
        var scheme = document.RootElement.TryGetProperty("scheme", out var property)
            ? property.GetString()
            : null;
        return new AuthToolPolicyViewModel(scheme);
    }

    private static IdempotencyToolPolicyViewModel? CreateIdempotencyPolicyViewModel(string? idempotencyPolicy)
    {
        if (string.IsNullOrWhiteSpace(idempotencyPolicy))
        {
            return null;
        }

        var normalized = ToolIdempotencyPolicyHelper.NormalizeOptionalPolicy(
            idempotencyPolicy,
            nameof(idempotencyPolicy));
        if (string.Equals(normalized, "idempotent", StringComparison.Ordinal))
        {
            return new IdempotencyToolPolicyViewModel(true);
        }

        if (string.Equals(normalized, "non-idempotent", StringComparison.Ordinal))
        {
            return new IdempotencyToolPolicyViewModel(false);
        }

        using var document = System.Text.Json.JsonDocument.Parse(normalized!);
        var enabled = document.RootElement.TryGetProperty("enabled", out var property)
            ? property.ValueKind == System.Text.Json.JsonValueKind.True
            : true;
        return new IdempotencyToolPolicyViewModel(enabled);
    }

    private static CompensationToolPolicyViewModel? CreateCompensationPolicyViewModel(string? compensationPolicy)
    {
        if (string.IsNullOrWhiteSpace(compensationPolicy))
        {
            return null;
        }

        var normalized = ToolStructuredPolicyHelper.NormalizeOptionalObjectOrLegacyString(
            compensationPolicy,
            nameof(compensationPolicy),
            "mode");
        using var document = System.Text.Json.JsonDocument.Parse(normalized!);
        var mode = document.RootElement.TryGetProperty("mode", out var property)
            ? property.GetString()
            : null;
        return new CompensationToolPolicyViewModel(mode);
    }

    private static ParameterToolPolicyViewModel? CreateParameterPolicyViewModel(string? parameterPolicy)
    {
        var parsed = ToolParameterPolicyHelper.ParseOptional(parameterPolicy);
        return parsed is null
            ? null
            : new ParameterToolPolicyViewModel(parsed.AllowedPaths, parsed.DeniedPaths);
    }

    private sealed class FakeMediator : IMediator
    {
        private readonly Func<object, object?> _handler;

        public FakeMediator(Func<GetToolDefinitionsQuery, IReadOnlyList<ToolDefinitionViewModel>> handler)
        {
            _handler = request => request is GetToolDefinitionsQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetToolDefinitionByNameQuery, ToolDefinitionViewModel?> handler)
        {
            _handler = request => request is GetToolDefinitionByNameQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<CreateToolDefinitionCommand, ToolDefinitionViewModel> handler)
        {
            _handler = request => request is CreateToolDefinitionCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<UpdateToolDefinitionCommand, ToolDefinitionViewModel> handler)
        {
            _handler = request => request is UpdateToolDefinitionCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Action<DeleteToolDefinitionCommand> handler)
        {
            _handler = request =>
            {
                if (request is DeleteToolDefinitionCommand command)
                {
                    handler(command);
                    return Unit.Value;
                }

                throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
            };
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((TResponse)_handler(request)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = _handler(request!);
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_handler(request));
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            return Task.CompletedTask;
        }
    }
}
