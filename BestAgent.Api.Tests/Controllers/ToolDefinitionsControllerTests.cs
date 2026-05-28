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
                CreateViewModel(toolName: "weather", enabled: true, hasHandler: false)
            ];
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetAll(true, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetToolDefinitionResponse>>(okResult.Value);
        var tool = Assert.Single(response);
        Assert.Equal("weather", tool.ToolName);
        Assert.Equal("Weather", tool.DisplayName);
        Assert.Equal("https://example.com/tools/weather", tool.EndpointUrl);
        Assert.Equal("POST", tool.HttpMethod);
        Assert.Equal("{\"Authorization\":\"Bearer token\"}", tool.AuthHeaders);
        Assert.Equal(5000, tool.TimeoutMs);
        Assert.True(tool.Enabled);
        Assert.False(tool.HasHandler);
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
            return CreateViewModel(toolName: "weather", enabled: true, hasHandler: true);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByName("weather", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.Equal("weather", response.ToolName);
        Assert.True(response.HasHandler);
    }

    [Fact]
    public async Task Create_ShouldMapRequestToCommand_AndReturnCreatedResponse()
    {
        var request = CreateRequest();
        var mediator = new FakeMediator((CreateToolDefinitionCommand command) =>
        {
            Assert.Equal("weather", command.ToolName);
            Assert.Equal("Weather", command.DisplayName);
            Assert.Equal("https://example.com/tools/weather", command.EndpointUrl);
            Assert.Equal("POST", command.HttpMethod);
            Assert.Equal("{\"Authorization\":\"Bearer token\"}", command.AuthHeaders);
            Assert.Equal(5000, command.TimeoutMs);
            Assert.True(command.AsyncSupported);
            Assert.True(command.Enabled);
            return CreateViewModel(toolName: command.ToolName, enabled: command.Enabled, hasHandler: false);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal("/tool-definitions/weather", createdResult.Location);
        var response = Assert.IsType<GetToolDefinitionResponse>(createdResult.Value);
        Assert.Equal("weather", response.ToolName);
        Assert.Equal("Weather", response.DisplayName);
    }

    [Fact]
    public async Task Update_ShouldMapRouteAndRequestToCommand_AndReturnOkResponse()
    {
        var request = new UpdateToolDefinitionRequest(
            "Weather v2",
            "updated description",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            "https://example.com/tools/weather-v2",
            "PUT",
            "{\"Authorization\":\"Bearer next-token\"}",
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
            Assert.Equal("https://example.com/tools/weather-v2", command.EndpointUrl);
            Assert.Equal("PUT", command.HttpMethod);
            Assert.Equal("{\"Authorization\":\"Bearer next-token\"}", command.AuthHeaders);
            Assert.Equal(8000, command.TimeoutMs);
            Assert.False(command.AsyncSupported);
            Assert.False(command.Enabled);
            return CreateViewModel(
                id: command.Id,
                toolName: "weather",
                displayName: command.DisplayName,
                endpointUrl: command.EndpointUrl,
                httpMethod: command.HttpMethod ?? "POST",
                authHeaders: command.AuthHeaders,
                timeoutMs: command.TimeoutMs,
                asyncSupported: command.AsyncSupported,
                enabled: command.Enabled,
                hasHandler: false);
        });
        var controller = new ToolDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Update("tool-001", request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetToolDefinitionResponse>(okResult.Value);
        Assert.Equal("tool-001", response.Id);
        Assert.Equal("Weather v2", response.DisplayName);
        Assert.Equal("https://example.com/tools/weather-v2", response.EndpointUrl);
        Assert.Equal("PUT", response.HttpMethod);
        Assert.Equal(8000, response.TimeoutMs);
        Assert.False(response.AsyncSupported);
        Assert.False(response.Enabled);
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
            "https://example.com/tools/weather",
            "POST",
            "{\"Authorization\":\"Bearer token\"}",
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
        string? endpointUrl = "https://example.com/tools/weather",
        string httpMethod = "POST",
        string? authHeaders = "{\"Authorization\":\"Bearer token\"}",
        int timeoutMs = 5000,
        bool asyncSupported = true,
        bool enabled = true,
        bool hasHandler = false)
    {
        var now = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
        return new ToolDefinitionViewModel(
            id,
            toolName,
            displayName,
            "Get weather",
            "{\"type\":\"object\"}",
            "{\"type\":\"string\"}",
            endpointUrl,
            httpMethod,
            authHeaders,
            "ReadOnly",
            timeoutMs,
            "retry-once",
            "bearer",
            "idempotent",
            asyncSupported,
            "Strong",
            "none",
            enabled,
            hasHandler,
            now,
            now);
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
