using BestAgent.Application.Tools;
using BestAgent.Application.Observability;
using BestAgent.Api.Tests.Observability;
using BestAgent.Domain.Tools;
using BestAgent.Infrastructure.Tools;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.Tools;

public class ToolExecutorTests
{
    private readonly InMemoryToolHandlerRegistry _toolRegistry = new();
    private readonly IToolDefinitionRepository _toolDefinitionRepository = Substitute.For<IToolDefinitionRepository>();
    private readonly IHttpToolInvoker _httpToolInvoker = Substitute.For<IHttpToolInvoker>();
    private readonly IAgentMetrics _agentMetrics = Substitute.For<IAgentMetrics>();
    private readonly ToolExecutionContext _context = new("run-001", "writer", "say hi");

    [Fact]
    public async Task ExecuteAsync_ShouldPreferToolDefinitionWebhookOverRegisteredHandler()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        var definition = CreateToolDefinition();
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
        await _httpToolInvoker.Received(1).InvokeAsync(
            Arg.Is<HttpToolInvocationRequest>(request =>
                request.ToolName == "weather" &&
                request.EndpointUrl == "https://example.com/tools/weather"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseExplicitWebhookBinding_WhenPresent()
    {
        var definition = CreateToolDefinition(
            endpointUrl: null,
            executionKind: ToolExecutionBindingHelper.Webhook,
            executionBinding: ToolExecutionBindingHelper.CreateWebhookBinding(
                "https://binding.example.com/weather",
                "PATCH",
                "{\"Authorization\":\"Bearer binding-token\"}"));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
        await _httpToolInvoker.Received(1).InvokeAsync(
            Arg.Is<HttpToolInvocationRequest>(request =>
                request.EndpointUrl == "https://binding.example.com/weather" &&
                request.HttpMethod == "PATCH" &&
                request.AuthHeaders == "{\"Authorization\":\"Bearer binding-token\"}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseExplicitLocalHandlerBinding_WhenPresent()
    {
        _toolRegistry.RegisterHandler("custom-weather-handler", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-custom-handler")));
        var definition = CreateToolDefinition(
            endpointUrl: null,
            executionKind: ToolExecutionBindingHelper.LocalHandler,
            executionBinding: ToolExecutionBindingHelper.CreateLocalHandlerBinding("custom-weather-handler"));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("from-custom-handler", result.Output);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseExplicitInlineResultBinding_WhenPresent()
    {
        var definition = CreateToolDefinition(
            endpointUrl: null,
            executionKind: ToolExecutionBindingHelper.InlineResult,
            executionBinding: ToolExecutionBindingHelper.CreateInlineResultBinding(
                "{\"temperature\":26.5}",
                "{\"source\":\"inline\"}"),
            outputSchema: "{\"type\":\"object\",\"required\":[\"temperature\"]}");
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("{\"temperature\":26.5}", result.Output);
        Assert.Equal("{\"source\":\"inline\"}", result.Meta);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionDisabledEvenIfHandlerExists()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(enabled: false));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' is disabled.", exception.Message);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionHasNoExplicitBindingAndNoLegacyEndpoint()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(CreateToolDefinition(endpointUrl: "   "));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' is defined but has no explicit execution binding configured.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionMissingEvenIfHandlerExists()
    {
        _toolRegistry.RegisterHandler("weather", (_, _, _) =>
            Task.FromResult(ToolExecutionResult.Completed("weather", "from-handler")));
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' has no persisted tool definition.", exception.Message);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenToolDefinitionAndHandlerMissing()
    {
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns((ToolDefinition?)null);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "input", _context, CancellationToken.None));

        Assert.Equal("Tool 'weather' has no persisted tool definition.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldInvokeHttpTool_WhenToolDefinitionExists()
    {
        var definition = CreateToolDefinition();
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        using var collector = new ActivityTestCollector(AgentTracing.SourceName);
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
        await _httpToolInvoker.Received(1).InvokeAsync(
            Arg.Is<HttpToolInvocationRequest>(request =>
                request.ToolName == "weather" &&
                request.EndpointUrl == "https://example.com/tools/weather" &&
                request.HttpMethod == "POST" &&
                request.AuthHeaders == "{\"Authorization\":\"Bearer token\"}" &&
                request.IdempotencyKey == null &&
                request.Input == "{\"city\":\"Shanghai\"}" &&
                request.InputSchema == "{\"type\":\"object\"}" &&
                request.OutputSchema == "{\"type\":\"string\"}" &&
                request.RetryPolicy == "{\"maxAttempts\":2}" &&
                request.Context == _context &&
                request.TimeoutMs == 5000),
            Arg.Any<CancellationToken>());
        _agentMetrics.Received(1).RecordToolExecution("weather", "completed", Arg.Any<TimeSpan>());
        var activity = Assert.Single(collector.Activities, value => value.OperationName == AgentTracing.ToolExecutionActivityName);
        Assert.Equal("weather", activity.GetTagItem("bestagent.tool"));
        Assert.Equal("run-001", activity.GetTagItem("bestagent.run_id"));
        Assert.Equal("writer", activity.GetTagItem("bestagent.agent_code"));
        Assert.Equal("completed", activity.GetTagItem("bestagent.status"));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputMissingRequiredSchemaProperty()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["city"],
              "properties": {
                "city": { "type": "string" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' is missing required property 'city'.", exception.Message);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputWithSchemaTypeMismatch()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["city"],
              "properties": {
                "city": { "type": "string" },
                "days": { "type": "integer" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\",\"days\":\"tomorrow\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.days' must be integer.", exception.Message);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputWithUnexpectedSchemaProperty()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "properties": {
                "city": { "type": "string" }
              },
              "additionalProperties": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\",\"debug\":true}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' contains unexpected property 'debug'.", exception.Message);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowInputMatchingSchema()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["city", "unit"],
              "properties": {
                "city": { "type": "string" },
                "unit": { "enum": ["celsius", "fahrenheit"] },
                "days": { "type": "integer" }
              },
              "additionalProperties": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\",\"unit\":\"celsius\",\"days\":2}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
        await _httpToolInvoker.Received(1).InvokeAsync(
            Arg.Is<HttpToolInvocationRequest>(request => request.Input == "{\"city\":\"Shanghai\",\"unit\":\"celsius\",\"days\":2}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputWithDeniedParameterPath()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "properties": {
                "city": { "type": "string" },
                "debug": { "type": "boolean" }
              }
            }
            """,
            parameterPolicy: "{\"allowedPaths\":[],\"deniedPaths\":[\"debug\"]}");
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\",\"debug\":true}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' contains denied parameter path '$.debug'.", exception.Message);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputOutsideAllowedParameterPaths()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "properties": {
                "city": { "type": "string" },
                "unit": { "type": "string" },
                "days": { "type": "integer" }
              }
            }
            """,
            parameterPolicy: "{\"allowedPaths\":[\"city\",\"unit\"],\"deniedPaths\":[]}");
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\",\"unit\":\"celsius\",\"days\":2}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' contains parameter path '$.days' which is not allowed by parameter policy.", exception.Message);
        await _httpToolInvoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputArrayItemsThatDoNotMatchSchema()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "properties": {
                "cities": {
                  "type": "array",
                  "minItems": 1,
                  "items": { "type": "string", "minLength": 2 }
                }
              },
              "required": ["cities"]
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"cities\":[\"SH\",3]}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.cities[1]' must be string.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowInputArrayMatchingPrefixItemsSchema()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["coordinates"],
              "properties": {
                "coordinates": {
                  "type": "array",
                  "prefixItems": [
                    { "type": "number", "minimum": -90, "maximum": 90 },
                    { "type": "number", "minimum": -180, "maximum": 180 }
                  ],
                  "items": false
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"coordinates\":[31.2,121.5]}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputArrayTupleItemNotMatchingPrefixItemsSchema()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["coordinates"],
              "properties": {
                "coordinates": {
                  "type": "array",
                  "prefixItems": [
                    { "type": "number" },
                    { "type": "number" }
                  ],
                  "items": false
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"coordinates\":[31.2,\"east\"]}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.coordinates[1]' must be number.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowInputArrayMatchingLegacyTupleItemsSchema()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["window"],
              "properties": {
                "window": {
                  "type": "array",
                  "items": [
                    { "type": "string", "const": "range" },
                    { "type": "integer", "minimum": 1 }
                  ],
                  "additionalItems": false
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"window\":[\"range\",3]}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputArrayWithExtraTupleItems_WhenAdditionalItemsDisabled()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["window"],
              "properties": {
                "window": {
                  "type": "array",
                  "items": [
                    { "type": "string" },
                    { "type": "integer" }
                  ],
                  "additionalItems": false
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"window\":[\"range\",3,true]}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.window[2]' is not allowed by tuple schema.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputArrayExtraTupleItemsViolatingAdditionalItemsSchema()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["window"],
              "properties": {
                "window": {
                  "type": "array",
                  "prefixItems": [
                    { "type": "string" },
                    { "type": "integer" }
                  ],
                  "additionalItems": { "type": "string", "minLength": 2 }
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"window\":[\"range\",3,1]}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.window[2]' must be string.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputNumberBelowMinimum()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["days"],
              "properties": {
                "days": { "type": "integer", "minimum": 1, "maximum": 7 }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"days\":0}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.days' must be >= 1.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputNumberNotMatchingMultipleOf()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["step"],
              "properties": {
                "step": { "type": "number", "multipleOf": 0.5 }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"step\":1.3}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.step' must be a multiple of 0.5.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputEmailNotMatchingFormat()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["email"],
              "properties": {
                "email": { "type": "string", "format": "email" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"email\":\"not-an-email\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.email' must match format 'email'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputIpv4NotMatchingFormat()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["ipAddress"],
              "properties": {
                "ipAddress": { "type": "string", "format": "ipv4" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"ipAddress\":\"2001:db8::1\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.ipAddress' must match format 'ipv4'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputArrayWithDuplicateItems_WhenUniqueItemsEnabled()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["cities"],
              "properties": {
                "cities": {
                  "type": "array",
                  "uniqueItems": true
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"cities\":[\"Shanghai\",\"Shanghai\"]}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.cities' must contain unique items.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputArrayMissingContainsMatch()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["temperatures"],
              "properties": {
                "temperatures": {
                  "type": "array",
                  "contains": { "type": "number", "minimum": 30 }
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"temperatures\":[20,25,28]}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.temperatures' must contain at least one item matching schema in 'contains'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputArrayWithTooFewContainsMatches_WhenMinContainsSpecified()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["temperatures"],
              "properties": {
                "temperatures": {
                  "type": "array",
                  "contains": { "type": "number", "minimum": 30 },
                  "minContains": 2
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"temperatures\":[20,31,28]}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.temperatures' must contain at least 2 items matching schema in 'contains'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputArrayWithTooManyContainsMatches_WhenMaxContainsSpecified()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["temperatures"],
              "properties": {
                "temperatures": {
                  "type": "array",
                  "contains": { "type": "number", "minimum": 30 },
                  "maxContains": 1
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"temperatures\":[31,35,28]}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.temperatures' must contain at most 1 items matching schema in 'contains'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputMissingDependentRequiredProperty()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "dependentRequired": {
                "city": ["country"]
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' is missing dependent property 'country' required by 'city'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputViolatingDependentSchemas()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "dependentSchemas": {
                "city": {
                  "required": ["country"]
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' is missing required property 'country'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowInputMatchingPatternProperties_WhenAdditionalPropertiesDisabled()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "patternProperties": {
                "^metric_[a-z]+$": { "type": "number" }
              },
              "additionalProperties": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"metric_temp\":26.5}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputAdditionalProperty_WhenAdditionalPropertiesFalseWithoutDeclaredProperties()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "additionalProperties": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"debug\":true}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' contains unexpected property 'debug'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputAdditionalPropertyValueViolatingAdditionalPropertiesSchema()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "properties": {
                "city": { "type": "string" }
              },
              "additionalProperties": {
                "type": "number"
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\",\"rank\":\"first\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.rank' must be number.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputViolatingThenBranch()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "if": {
                "required": ["unit"],
                "properties": {
                  "unit": { "const": "celsius" }
                }
              },
              "then": {
                "required": ["scale"],
                "properties": {
                  "scale": { "const": "metric" }
                }
              },
              "else": {
                "required": ["scale"],
                "properties": {
                  "scale": { "const": "imperial" }
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"unit\":\"celsius\",\"scale\":\"imperial\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.scale' must match const value.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowInputDateTimeMatchingFormat()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["scheduledAt"],
              "properties": {
                "scheduledAt": { "type": "string", "format": "date-time" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(
            "weather",
            "{\"scheduledAt\":\"2026-06-08T10:30:00Z\"}",
            _context,
            CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowInputHostnameMatchingFormat()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["host"],
              "properties": {
                "host": { "type": "string", "format": "hostname" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(
            "weather",
            "{\"host\":\"api.internal.example\"}",
            _context,
            CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputStringNotMatchingPattern()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["ticketCode"],
              "properties": {
                "ticketCode": { "type": "string", "pattern": "^[A-Z]{3}-\\d{4}$" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"ticketCode\":\"bad-1\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.ticketCode' must match pattern '^[A-Z]{3}-\\d{4}$'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputObjectWithInvalidPropertyName()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "propertyNames": {
                "pattern": "^[a-z_]+$"
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"Bad-Key\":\"value\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.<Bad-Key>' must match pattern '^[a-z_]+$'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputObjectBelowMinProperties()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "minProperties": 2,
              "properties": {
                "city": { "type": "string" },
                "unit": { "type": "string" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$' must have at least 2 properties.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputNotMatchingConst()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["region"],
              "properties": {
                "region": { "const": "cn-east-1" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"region\":\"us-west-1\"}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.region' must match const value.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowInputMatchingAnyOf()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["target"],
              "properties": {
                "target": {
                  "anyOf": [
                    { "type": "string", "minLength": 3 },
                    { "type": "integer", "minimum": 1 }
                  ]
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "from-webhook"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"target\":3}", _context, CancellationToken.None);

        Assert.Equal("from-webhook", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputNumberAtExclusiveMinimumBoundary()
    {
        var definition = CreateToolDefinition(
            inputSchema:
            """
            {
              "type": "object",
              "required": ["temperature"],
              "properties": {
                "temperature": { "type": "number", "exclusiveMinimum": 0 }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"temperature\":0}", _context, CancellationToken.None));

        Assert.Equal("Input for tool 'weather' at '$.temperature' must be > 0.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputThatDoesNotMatchSchema()
    {
        var definition = CreateToolDefinition(outputSchema: "{\"type\":\"object\",\"required\":[\"temperature\"]}");
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "\"plain-text\""));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must be object.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowOutputMatchingObjectSchema()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "required": ["temperature"],
              "properties": {
                "temperature": { "type": "number" },
                "unit": { "enum": ["celsius", "fahrenheit"] }
              },
              "additionalProperties": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"temperature\":26.5,\"unit\":\"celsius\"}"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("{\"temperature\":26.5,\"unit\":\"celsius\"}", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputMatchingMultipleOneOfCandidates()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "oneOf": [
                { "type": "number" },
                { "enum": [1] }
              ]
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "1"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must match exactly one schema in 'oneOf'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputMatchingNotSchema()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "not": {
                "type": "object",
                "required": ["error"]
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"error\":\"denied\"}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must not match schema in 'not'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputNumberAtExclusiveMaximumBoundary()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "number",
              "exclusiveMaximum": 10
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "10"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must be < 10.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputNumberNotMatchingMultipleOf()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "number",
              "multipleOf": 0.25
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "0.3"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must be a multiple of 0.25.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputStringExceedingMaxLength()
    {
        var definition = CreateToolDefinition(outputSchema: "{\"type\":\"string\",\"maxLength\":5}");
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "\"too-long\""));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must have length <= 5.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputArrayWithDuplicateItems_WhenUniqueItemsEnabled()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "uniqueItems": true
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[1,1]"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must contain unique items.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputArrayMissingContainsMatch()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "contains": { "type": "integer", "minimum": 10 }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[1,2,3]"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must contain at least one item matching schema in 'contains'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputArrayWithTooFewContainsMatches_WhenMinContainsSpecified()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "contains": { "type": "integer", "minimum": 10 },
              "minContains": 2
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[10,2,3]"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must contain at least 2 items matching schema in 'contains'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputArrayWithTooManyContainsMatches_WhenMaxContainsSpecified()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "contains": { "type": "integer", "minimum": 10 },
              "maxContains": 1
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[10,12,3]"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must contain at most 1 items matching schema in 'contains'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputUriNotMatchingFormat()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "required": ["downloadUrl"],
              "properties": {
                "downloadUrl": { "type": "string", "format": "uri" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"downloadUrl\":\"not-a-uri\"}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$.downloadUrl' must match format 'uri'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowOutputUriReferenceMatchingFormat()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "required": ["downloadPath"],
              "properties": {
                "downloadPath": { "type": "string", "format": "uri-reference" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"downloadPath\":\"../downloads/result.json?version=1#part\"}"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("{\"downloadPath\":\"../downloads/result.json?version=1#part\"}", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputObjectWithInvalidPropertyName()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "propertyNames": {
                "pattern": "^[a-z_]+$"
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"Bad-Key\":\"value\"}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$.<Bad-Key>' must match pattern '^[a-z_]+$'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputPatternPropertyValueNotMatchingSchema()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "patternProperties": {
                "^metric_[a-z]+$": { "type": "number" }
              },
              "additionalProperties": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"metric_temp\":\"hot\"}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$.metric_temp' must be number.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputViolatingDependentSchemas()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "dependentSchemas": {
                "status": {
                  "required": ["payload"]
                }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"status\":\"ok\"}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' is missing required property 'payload'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputViolatingElseBranch()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "if": {
                "required": ["status"],
                "properties": {
                  "status": { "const": "ok" }
                }
              },
              "then": {
                "required": ["payload"]
              },
              "else": {
                "required": ["error"]
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"status\":\"failed\",\"payload\":{}}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' is missing required property 'error'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputStringNotMatchingPattern()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "required": ["ticketCode"],
              "properties": {
                "ticketCode": { "type": "string", "pattern": "^[A-Z]{3}-\\d{4}$" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"ticketCode\":\"bad-1\"}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$.ticketCode' must match pattern '^[A-Z]{3}-\\d{4}$'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputObjectAboveMaxProperties()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "maxProperties": 1,
              "properties": {
                "temperature": { "type": "number" },
                "unit": { "type": "string" }
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"temperature\":26.5,\"unit\":\"celsius\"}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$' must have at most 1 properties.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputAdditionalPropertyValueViolatingAdditionalPropertiesSchema()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "object",
              "properties": {
                "temperature": { "type": "number" }
              },
              "additionalProperties": {
                "type": "string"
              }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "{\"temperature\":26.5,\"humidity\":60}"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$.humidity' must be string.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowOutputArrayMatchingItemsSchema()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "minItems": 2,
              "items": { "type": "integer", "minimum": 1 }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[1,2,3]"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("[1,2,3]", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowOutputArrayMatchingPrefixItemsSchema()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "prefixItems": [
                { "type": "string", "const": "ok" },
                { "type": "integer", "minimum": 1 }
              ],
              "items": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[\"ok\",2]"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.Equal("[\"ok\",2]", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputArrayTupleItemNotMatchingPrefixItemsSchema()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "prefixItems": [
                { "type": "string" },
                { "type": "integer" }
              ],
              "items": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[\"ok\",\"bad\"]"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$[1]' must be integer.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputArrayWithExtraTupleItems_WhenAdditionalItemsDisabled()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "items": [
                { "type": "string" },
                { "type": "integer" }
              ],
              "additionalItems": false
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[\"ok\",2,true]"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$[2]' is not allowed by tuple schema.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectOutputArrayExtraTupleItemsViolatingAdditionalItemsSchema()
    {
        var definition = CreateToolDefinition(
            outputSchema:
            """
            {
              "type": "array",
              "prefixItems": [
                { "type": "string" },
                { "type": "integer" }
              ],
              "additionalItems": { "type": "string", "minLength": 2 }
            }
            """);
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Completed("weather", "[\"ok\",2,1]"));
        var executor = CreateExecutor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None));

        Assert.Equal("Output for tool 'weather' at '$[2]' must be string.", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipOutputValidation_WhenToolResultIsPending()
    {
        var definition = CreateToolDefinition(outputSchema: "{\"type\":\"object\",\"required\":[\"temperature\"]}");
        _toolDefinitionRepository.GetByToolNameAsync("weather", Arg.Any<CancellationToken>())
            .Returns(definition);
        _httpToolInvoker.InvokeAsync(Arg.Any<HttpToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Pending("weather", "wait-123"));
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync("weather", "{\"city\":\"Shanghai\"}", _context, CancellationToken.None);

        Assert.True(result.IsPending);
        Assert.Equal("wait-123", result.WaitToken);
        _agentMetrics.Received(1).RecordToolExecution("weather", "pending", Arg.Any<TimeSpan>());
    }

    private ToolExecutor CreateExecutor()
    {
        return new ToolExecutor(
            new ToolResolver(_toolRegistry, _toolDefinitionRepository),
            _httpToolInvoker,
            new JsonSchemaToolInputValidator(),
            new JsonSchemaToolOutputValidator(),
            _agentMetrics);
    }

    private static ToolDefinition CreateToolDefinition(
        bool enabled = true,
        string? endpointUrl = "https://example.com/tools/weather",
        string? executionKind = null,
        string? executionBinding = null,
        string? inputSchema = "{\"type\":\"object\"}",
        string? outputSchema = "{\"type\":\"string\"}",
        string? parameterPolicy = null)
    {
        if (executionKind is null
            && executionBinding is null
            && !string.IsNullOrWhiteSpace(endpointUrl))
        {
            executionKind = ToolExecutionBindingHelper.Webhook;
            executionBinding = ToolExecutionBindingHelper.CreateWebhookBinding(
                endpointUrl.Trim(),
                "POST",
                "{\"Authorization\":\"Bearer token\"}");
        }

        return new ToolDefinition
        {
            Id = "tool-001",
            ToolName = "weather",
            DisplayName = "Weather",
            Enabled = enabled,
            ExecutionKind = executionKind,
            ExecutionBinding = executionBinding,
            EndpointUrl = endpointUrl,
            HttpMethod = "POST",
            AuthHeaders = "{\"Authorization\":\"Bearer token\"}",
            InputSchema = inputSchema,
            OutputSchema = outputSchema,
            ParameterPolicy = parameterPolicy,
            RetryPolicy = "{\"maxAttempts\":2}",
            TimeoutMs = 5000
        };
    }
}
