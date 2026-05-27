using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Api.Controllers;
using BestAgent.Api.Mappings;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Runtime;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BestAgent.Api.Tests.Controllers;

public class AgentRunsControllerTests
{
    private readonly IMapper _mapper;

    public AgentRunsControllerTests()
    {
        var configuration = new MapperConfiguration(config =>
        {
            config.AddProfile<ApiMappingProfile>();
        }, NullLoggerFactory.Instance);

        _mapper = configuration.CreateMapper();
    }

    [Fact]
    public async Task Create()
    {
        var request = new CreateAgentRunRequest("writer", "hello");
        var mediator = new FakeMediator((CreateAgentRunCommand command) =>
        {
            Assert.Equal("writer", command.AgentCode);
            Assert.Equal("hello", command.Input);

            return new CreateAgentRunResult("run-001", command.AgentCode, command.Input, "done", "Succeeded");
        });
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal("/agent-runs/run-001", createdResult.Location);

        var response = Assert.IsType<CreateAgentRunResponse>(createdResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("writer", response.AgentCode);
        Assert.Equal("hello", response.Input);
        Assert.Equal("done", response.Output);
        Assert.Equal("Succeeded", response.Status);
    }

    [Fact]
    public async Task GetById()
    {
        var now = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunByIdQuery query) =>
            new GetAgentRunByIdResult(
                query.RunId,
                "writer",
                "Succeeded",
                "hello",
                "done",
                10,
                100,
                12.5m,
                now,
                now,
                now,
                now));
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetById("run-001", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetAgentRunResponse>(okResult.Value);
        Assert.Equal("run-001", response.RunId);
        Assert.Equal("writer", response.AgentCode);
        Assert.Equal("Succeeded", response.Status);
        Assert.Equal("hello", response.Input);
        Assert.Equal("done", response.Output);
        Assert.Equal(10, response.MaxTurns);
        Assert.Equal(100, response.MaxCost);
        Assert.Equal(12.5m, response.TotalCost);
    }

    [Fact]
    public async Task GetSteps()
    {
        var now = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentRunStepsQuery query) =>
            (IReadOnlyList<GetAgentRunStepsItem>)
            [
                new(
                    "step-001",
                    1,
                    "Plan",
                    "Succeeded",
                    $"input:{query.RunId}",
                    "output",
                    null,
                    "plan",
                    now,
                    now,
                    now,
                    now,
                    1200)
            ]);
        var controller = new AgentRunsController(mediator, _mapper, new NullEventBus());

        var actionResult = await controller.GetSteps("run-001", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetAgentRunStepResponse>>(okResult.Value);
        var step = Assert.Single(response);
        Assert.Equal("step-001", step.StepId);
        Assert.Equal(1, step.StepNo);
        Assert.Equal("Plan", step.StepType);
        Assert.Equal("Succeeded", step.Status);
        Assert.Equal("input:run-001", step.Input);
        Assert.Equal("output", step.Output);
        Assert.Equal("plan", step.StepKey);
        Assert.Equal(1200, step.DurationMs);
    }

    private sealed class NullEventBus : IAgentRunEventBus
    {
        public void Publish(AgentRunEvent evt) { }

        public async IAsyncEnumerable<AgentRunEvent> SubscribeAsync(
            string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeMediator : IMediator
    {
        private readonly Func<object, object?> _handler;

        public FakeMediator(Func<CreateAgentRunCommand, CreateAgentRunResult> handler)
        {
            _handler = request => request is CreateAgentRunCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentRunByIdQuery, GetAgentRunByIdResult?> handler)
        {
            _handler = request => request is GetAgentRunByIdQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentRunStepsQuery, IReadOnlyList<GetAgentRunStepsItem>> handler)
        {
            _handler = request => request is GetAgentRunStepsQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
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
