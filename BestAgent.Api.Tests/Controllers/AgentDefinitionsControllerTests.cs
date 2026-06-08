using AutoMapper;
using BestAgent.Api.Contracts.AgentDefinitions;
using BestAgent.Api.Controllers;
using BestAgent.Api.Mappings;
using BestAgent.Application.AgentDefinitions;
using BestAgent.Application.AgentDefinitions.Commands.ActivateAgentDefinitionVersion;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinition;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinitionVersion;
using BestAgent.Application.AgentDefinitions.Commands.CreateRouteRule;
using BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitionByCode;
using BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitions;
using BestAgent.Application.AgentDefinitions.Queries.GetAgentDefinitionVersions;
using BestAgent.Application.AgentDefinitions.Queries.GetRouteRules;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BestAgent.Api.Tests.Controllers;

public class AgentDefinitionsControllerTests
{
    private readonly IMapper _mapper;

    public AgentDefinitionsControllerTests()
    {
        var configuration = new MapperConfiguration(config =>
        {
            config.AddProfile<ApiMappingProfile>();
        }, NullLoggerFactory.Instance);

        _mapper = configuration.CreateMapper();
    }

    [Fact]
    public async Task GetAll_ShouldReturnMappedResponse()
    {
        var now = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator((GetAgentDefinitionsQuery _) =>
            (IReadOnlyList<AgentDefinitionViewModel>)
            [
                CreateDefinitionViewModel(code: "writer", name: "Writer", currentVersion: 2, publishedAt: now)
            ]);
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetAll(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetAgentDefinitionResponse>>(okResult.Value);
        var definition = Assert.Single(response);
        Assert.Equal("writer", definition.Code);
        Assert.Equal("Writer", definition.Name);
        Assert.Equal(2, definition.CurrentVersion);
        Assert.Equal("gpt-4.1", definition.DefaultModel);
        Assert.Equal(["weather", "search"], definition.AllowedTools);
        Assert.Equal(["faq", "travel-guide"], definition.KnowledgeSources);
        Assert.Equal("{\"includeKnowledge\":true}", definition.MemoryPolicy);
        Assert.Equal("{\"strategy\":\"single-agent\"}", definition.RoutingPolicy);
        Assert.Equal("{\"AllowedApproverRoles\":[\"ops\"]}", definition.ApprovalPolicy);
        Assert.Equal("{\"mode\":\"bounded\"}", definition.ExecutionPolicy);
        Assert.Equal("{\"planner\":\"default\"}", definition.PlannerPolicy);
        Assert.Equal("{\"citations\":true}", definition.ContextPolicy);
        Assert.Equal(["support_agent", "finance_agent"], definition.AllowedHandoffs);
        Assert.Equal("{\"type\":\"object\",\"required\":[\"answer\"]}", definition.OutputSchema);
    }

    [Fact]
    public async Task GetByCode_ShouldReturnNotFound_WhenDefinitionDoesNotExist()
    {
        var mediator = new FakeMediator((GetAgentDefinitionByCodeQuery _) => (AgentDefinitionViewModel?)null);
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByCode("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(actionResult.Result);
    }

    [Fact]
    public async Task GetByCode_ShouldReturnMappedResponse_WhenDefinitionExists()
    {
        var mediator = new FakeMediator((GetAgentDefinitionByCodeQuery query) =>
        {
            Assert.Equal("writer", query.AgentCode);
            return CreateDefinitionViewModel(code: query.AgentCode);
        });
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetByCode("writer", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetAgentDefinitionResponse>(okResult.Value);
        Assert.Equal("writer", response.Code);
        Assert.Equal("Writer", response.Name);
        Assert.Equal("system prompt", response.SystemPromptTemplate);
        Assert.Equal(["faq", "travel-guide"], response.KnowledgeSources);
        Assert.Equal("{\"includeKnowledge\":true}", response.MemoryPolicy);
        Assert.Equal("{\"strategy\":\"single-agent\"}", response.RoutingPolicy);
        Assert.Equal("{\"mode\":\"bounded\"}", response.ExecutionPolicy);
        Assert.Equal("{\"planner\":\"default\"}", response.PlannerPolicy);
        Assert.Equal("{\"citations\":true}", response.ContextPolicy);
        Assert.Equal(["support_agent", "finance_agent"], response.AllowedHandoffs);
        Assert.Equal("{\"type\":\"object\",\"required\":[\"answer\"]}", response.OutputSchema);
    }

    [Fact]
    public async Task Create_ShouldMapRequestToCommand_AndReturnCreatedResponse()
    {
        var request = new CreateAgentDefinitionRequest(
            "writer",
            "Writer",
            "writes content",
            "follow instructions",
            "system prompt",
            "gpt-4.1",
            ["weather", "search"],
            ["faq", "travel-guide"],
            """
            { "includeKnowledge": true }
            """,
            """
            { "strategy": "single-agent" }
            """,
            "{\"allowedApproverRoles\":[\"security\"]}",
            """
            { "mode": "bounded" }
            """,
            """
            { "planner": "default" }
            """,
            """
            { "citations": true }
            """,
            ["support_agent", "finance_agent"],
            """
            { "type": "object", "required": ["answer"] }
            """,
            8,
            12.5m,
            true,
            ["search"]);
        var mediator = new FakeMediator((CreateAgentDefinitionCommand command) =>
        {
            Assert.Equal("writer", command.Code);
            Assert.Equal("Writer", command.Name);
            Assert.Equal("writes content", command.Description);
            Assert.Equal("follow instructions", command.Instruction);
            Assert.Equal("system prompt", command.SystemPromptTemplate);
            Assert.Equal("gpt-4.1", command.DefaultModel);
            Assert.Equal(["weather", "search"], command.AllowedTools);
            Assert.Equal(["search"], command.DeniedTools);
            Assert.Equal(["faq", "travel-guide"], command.KnowledgeSources);
            Assert.Equal(
                """
                { "strategy": "single-agent" }
                """,
                command.RoutingPolicy);
            Assert.Equal("{\"allowedApproverRoles\":[\"security\"]}", command.ApprovalPolicy);
            Assert.Equal(
                """
                { "mode": "bounded" }
                """,
                command.ExecutionPolicy);
            Assert.Equal(
                """
                { "planner": "default" }
                """,
                command.PlannerPolicy);
            Assert.Equal(
                """
                { "citations": true }
                """,
                command.ContextPolicy);
            Assert.Equal(["support_agent", "finance_agent"], command.AllowedHandoffs);
            Assert.Equal(
                """
                { "type": "object", "required": ["answer"] }
                """,
                command.OutputSchema);
            Assert.Equal(
                """
                { "includeKnowledge": true }
                """,
                command.MemoryPolicy);
            Assert.Equal(8, command.MaxTurns);
            Assert.Equal(12.5m, command.MaxCost);
            Assert.True(command.Enabled);
            return CreateDefinitionViewModel(
                code: command.Code,
                name: command.Name,
                maxTurns: command.MaxTurns,
                maxCost: command.MaxCost,
                deniedTools: command.DeniedTools,
                memoryPolicy: "{\"includeKnowledge\":true}",
                routingPolicy: "{\"strategy\":\"single-agent\"}",
                approvalPolicy: "{\"AllowedApproverRoles\":[\"security\"]}",
                executionPolicy: "{\"mode\":\"bounded\"}",
                plannerPolicy: "{\"planner\":\"default\"}",
                contextPolicy: "{\"citations\":true}",
                allowedHandoffs: ["support_agent", "finance_agent"],
                outputSchema: "{\"type\":\"object\",\"required\":[\"answer\"]}");
        });
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal("/agent-definitions/writer", createdResult.Location);
        var response = Assert.IsType<GetAgentDefinitionResponse>(createdResult.Value);
        Assert.Equal("writer", response.Code);
        Assert.Equal("Writer", response.Name);
        Assert.Equal(8, response.MaxTurns);
        Assert.Equal(12.5m, response.MaxCost);
        Assert.Equal(["search"], response.DeniedTools);
        Assert.Equal(["faq", "travel-guide"], response.KnowledgeSources);
        Assert.Equal("{\"includeKnowledge\":true}", response.MemoryPolicy);
        Assert.Equal("{\"strategy\":\"single-agent\"}", response.RoutingPolicy);
        Assert.Equal("{\"AllowedApproverRoles\":[\"security\"]}", response.ApprovalPolicy);
        Assert.Equal("{\"mode\":\"bounded\"}", response.ExecutionPolicy);
        Assert.Equal("{\"planner\":\"default\"}", response.PlannerPolicy);
        Assert.Equal("{\"citations\":true}", response.ContextPolicy);
        Assert.Equal(["support_agent", "finance_agent"], response.AllowedHandoffs);
        Assert.Equal("{\"type\":\"object\",\"required\":[\"answer\"]}", response.OutputSchema);
    }

    [Fact]
    public async Task GetVersions_ShouldReturnNotFound_WhenDefinitionDoesNotExist()
    {
        var mediator = new FakeMediator((GetAgentDefinitionByCodeQuery _) => (AgentDefinitionViewModel?)null);
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetVersions("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(actionResult.Result);
    }

    [Fact]
    public async Task GetVersions_ShouldReturnMappedVersions_WhenDefinitionExists()
    {
        var now = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
        var mediator = new FakeMediator(
            getByCodeHandler: query => CreateDefinitionViewModel(code: query.AgentCode),
            getVersionsHandler: query =>
            {
                Assert.Equal("writer", query.AgentCode);
                return (IReadOnlyList<AgentDefinitionVersionViewModel>)
                [
                    CreateVersionViewModel(version: 1, isCurrentVersion: false, publishedAt: now.AddDays(-1)),
                    CreateVersionViewModel(version: 2, isCurrentVersion: true, publishedAt: now)
                ];
            });
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetVersions("writer", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetAgentDefinitionVersionResponse>>(okResult.Value);
        Assert.Equal(2, response.Count);
        Assert.Equal(1, response[0].Version);
        Assert.False(response[0].IsCurrentVersion);
        Assert.Equal(2, response[1].Version);
        Assert.True(response[1].IsCurrentVersion);
    }

    [Fact]
    public async Task CreateVersion_ShouldMapRouteAndRequestToCommand_AndReturnCreatedResponse()
    {
        var request = new CreateAgentDefinitionVersionRequest(
            "Writer v2",
            "improved",
            "follow instructions carefully",
            "system prompt v2",
            "gpt-4.1-mini",
            ["weather"],
            ["faq"],
            """
            { "includeSummary": false }
            """,
            """
            { "strategy": "handoff-first" }
            """,
            "{\"allowedApproverRoles\":[\"security\"]}",
            """
            { "mode": "strict" }
            """,
            """
            { "planner": "multi-step" }
            """,
            """
            { "contextWindow": "extended" }
            """,
            ["refund_agent"],
            """
            { "type": "string", "minLength": 3 }
            """,
            10,
            20m,
            ["search"]);
        var mediator = new FakeMediator((CreateAgentDefinitionVersionCommand command) =>
        {
            Assert.Equal("writer", command.AgentCode);
            Assert.Equal("Writer v2", command.Name);
            Assert.Equal("improved", command.Description);
            Assert.Equal("follow instructions carefully", command.Instruction);
            Assert.Equal("system prompt v2", command.SystemPromptTemplate);
            Assert.Equal("gpt-4.1-mini", command.DefaultModel);
            Assert.Equal(["weather"], command.AllowedTools);
            Assert.Equal(["search"], command.DeniedTools);
            Assert.Equal(["faq"], command.KnowledgeSources);
            Assert.Equal(
                """
                { "strategy": "handoff-first" }
                """,
                command.RoutingPolicy);
            Assert.Equal(
                """
                { "includeSummary": false }
                """,
                command.MemoryPolicy);
            Assert.Equal("{\"allowedApproverRoles\":[\"security\"]}", command.ApprovalPolicy);
            Assert.Equal(
                """
                { "mode": "strict" }
                """,
                command.ExecutionPolicy);
            Assert.Equal(
                """
                { "planner": "multi-step" }
                """,
                command.PlannerPolicy);
            Assert.Equal(
                """
                { "contextWindow": "extended" }
                """,
                command.ContextPolicy);
            Assert.Equal(["refund_agent"], command.AllowedHandoffs);
            Assert.Equal(
                """
                { "type": "string", "minLength": 3 }
                """,
                command.OutputSchema);
            Assert.Equal(10, command.MaxTurns);
            Assert.Equal(20m, command.MaxCost);
            return CreateVersionViewModel(
                version: 2,
                name: command.Name!,
                maxTurns: command.MaxTurns,
                maxCost: command.MaxCost,
                deniedTools: command.DeniedTools,
                knowledgeSources: ["faq"],
                memoryPolicy: "{\"includeSummary\":false}",
                routingPolicy: "{\"strategy\":\"handoff-first\"}",
                approvalPolicy: command.ApprovalPolicy,
                executionPolicy: "{\"mode\":\"strict\"}",
                plannerPolicy: "{\"planner\":\"multi-step\"}",
                contextPolicy: "{\"contextWindow\":\"extended\"}",
                allowedHandoffs: ["refund_agent"],
                outputSchema: "{\"type\":\"string\",\"minLength\":3}");
        });
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.CreateVersion("writer", request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal("/agent-definitions/writer/versions/2", createdResult.Location);
        var response = Assert.IsType<GetAgentDefinitionVersionResponse>(createdResult.Value);
        Assert.Equal(2, response.Version);
        Assert.Equal("Writer v2", response.Name);
        Assert.Equal("{\"allowedApproverRoles\":[\"security\"]}", response.ApprovalPolicy);
        Assert.Equal(["faq"], response.KnowledgeSources);
        Assert.Equal("{\"includeSummary\":false}", response.MemoryPolicy);
        Assert.Equal("{\"strategy\":\"handoff-first\"}", response.RoutingPolicy);
        Assert.Equal("{\"mode\":\"strict\"}", response.ExecutionPolicy);
        Assert.Equal("{\"planner\":\"multi-step\"}", response.PlannerPolicy);
        Assert.Equal("{\"contextWindow\":\"extended\"}", response.ContextPolicy);
        Assert.Equal(["refund_agent"], response.AllowedHandoffs);
        Assert.Equal("{\"type\":\"string\",\"minLength\":3}", response.OutputSchema);
        Assert.Equal(["search"], response.DeniedTools);
        Assert.Equal(10, response.MaxTurns);
        Assert.Equal(20m, response.MaxCost);
    }

    [Fact]
    public async Task ActivateVersion_ShouldSendCommand_AndReturnOkResponse()
    {
        var request = new ActivateAgentDefinitionVersionRequest(3);
        var mediator = new FakeMediator((ActivateAgentDefinitionVersionCommand command) =>
        {
            Assert.Equal("writer", command.AgentCode);
            Assert.Equal(3, command.Version);
            return CreateDefinitionViewModel(code: command.AgentCode, currentVersion: command.Version, version: command.Version, versionStatus: "Published", versionName: "Writer v3");
        });
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.ActivateVersion("writer", request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<GetAgentDefinitionResponse>(okResult.Value);
        Assert.Equal("writer", response.Code);
        Assert.Equal(3, response.CurrentVersion);
        Assert.Equal(3, response.Version);
        Assert.Equal("Published", response.VersionStatus);
    }

    [Fact]
    public async Task GetRouteRules_ShouldReturnMappedResponses()
    {
        var mediator = new FakeMediator((GetRouteRulesQuery query) =>
        {
            Assert.Equal("writer", query.AgentCode);
            Assert.Equal(2, query.Version);
            return (IReadOnlyList<RouteRuleViewModel>)
            [
                CreateRouteRuleViewModel("rule-1", "Support", 10),
                CreateRouteRuleViewModel("rule-2", "Finance", 20)
            ];
        });
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.GetRouteRules("writer", 2, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<GetRouteRuleResponse>>(okResult.Value);
        Assert.Equal(2, response.Count);
        Assert.Equal("rule-1", response[0].Id);
        Assert.Equal("Support", response[0].RuleName);
        Assert.Equal("delegate_and_wait", response[0].HandoffMode);
    }

    [Fact]
    public async Task CreateRouteRule_ShouldMapRouteAndRequestToCommand_AndReturnCreatedResponse()
    {
        var request = new CreateRouteRuleRequest(
            "support_agent",
            "Support",
            10,
            "intent",
            """
            { "intent": "support" }
            """,
            "route_only",
            """
            { "mode": "summary_only" }
            """,
            """
            { "mode": "read_only" }
            """,
            """
            { "inherit": false }
            """,
            """
            { "sources": ["faq"] }
            """,
            true,
            true);
        var mediator = new FakeMediator((CreateRouteRuleCommand command) =>
        {
            Assert.Equal("writer", command.AgentCode);
            Assert.Equal(2, command.Version);
            Assert.Equal("support_agent", command.TargetAgentCode);
            Assert.Equal("Support", command.RuleName);
            Assert.Equal("route_only", command.HandoffMode);
            return CreateRouteRuleViewModel("rule-1", command.RuleName, command.Priority);
        });
        var controller = new AgentDefinitionsController(mediator, _mapper);

        var actionResult = await controller.CreateRouteRule("writer", 2, request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal("/agent-definitions/writer/versions/2/route-rules/rule-1", createdResult.Location);
        var response = Assert.IsType<GetRouteRuleResponse>(createdResult.Value);
        Assert.Equal("rule-1", response.Id);
        Assert.Equal("Support", response.RuleName);
        Assert.Equal("delegate_and_wait", response.HandoffMode);
    }

    private static AgentDefinitionViewModel CreateDefinitionViewModel(
        string code = "writer",
        string name = "Writer",
        int currentVersion = 1,
        string versionId = "version-001",
        int version = 1,
        string versionStatus = "Published",
        string versionName = "Writer v1",
        int maxTurns = 8,
        decimal maxCost = 12.5m,
        IReadOnlyList<string>? deniedTools = null,
        IReadOnlyList<string>? knowledgeSources = null,
        string? memoryPolicy = "{\"includeKnowledge\":true}",
        string? routingPolicy = "{\"strategy\":\"single-agent\"}",
        string? approvalPolicy = "{\"AllowedApproverRoles\":[\"ops\"]}",
        string? executionPolicy = "{\"mode\":\"bounded\"}",
        string? plannerPolicy = "{\"planner\":\"default\"}",
        string? contextPolicy = "{\"citations\":true}",
        IReadOnlyList<string>? allowedHandoffs = null,
        string? outputSchema = "{\"type\":\"object\",\"required\":[\"answer\"]}",
        DateTime? publishedAt = null)
    {
        var now = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
        return new AgentDefinitionViewModel(
            code,
            name,
            "writes content",
            true,
            currentVersion,
            versionId,
            version,
            versionStatus,
            versionName,
            "version description",
            "follow instructions",
            "system prompt",
            "gpt-4.1",
            ["weather", "search"],
            knowledgeSources ?? ["faq", "travel-guide"],
            memoryPolicy,
            routingPolicy,
            approvalPolicy,
            executionPolicy,
            plannerPolicy,
            contextPolicy,
            allowedHandoffs ?? ["support_agent", "finance_agent"],
            outputSchema,
            maxTurns,
            maxCost,
            now,
            now,
            publishedAt,
            deniedTools ?? ["search"]);
    }

    private static AgentDefinitionVersionViewModel CreateVersionViewModel(
        string versionId = "version-001",
        int version = 1,
        string status = "Draft",
        string name = "Writer v1",
        int maxTurns = 8,
        decimal maxCost = 12.5m,
        IReadOnlyList<string>? deniedTools = null,
        IReadOnlyList<string>? knowledgeSources = null,
        string? memoryPolicy = "{\"includeKnowledge\":true}",
        string? routingPolicy = "{\"strategy\":\"single-agent\"}",
        string? approvalPolicy = "{\"AllowedApproverRoles\":[\"ops\"]}",
        string? executionPolicy = "{\"mode\":\"bounded\"}",
        string? plannerPolicy = "{\"planner\":\"default\"}",
        string? contextPolicy = "{\"citations\":true}",
        IReadOnlyList<string>? allowedHandoffs = null,
        string? outputSchema = "{\"type\":\"object\",\"required\":[\"answer\"]}",
        bool isCurrentVersion = false,
        DateTime? publishedAt = null)
    {
        var now = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
        return new AgentDefinitionVersionViewModel(
            versionId,
            version,
            status,
            name,
            "version description",
            "follow instructions",
            "system prompt",
            "gpt-4.1",
            ["weather", "search"],
            knowledgeSources ?? ["faq", "travel-guide"],
            memoryPolicy,
            routingPolicy,
            approvalPolicy,
            executionPolicy,
            plannerPolicy,
            contextPolicy,
            allowedHandoffs ?? ["support_agent", "finance_agent"],
            outputSchema,
            maxTurns,
            maxCost,
            isCurrentVersion,
            now,
            now,
            publishedAt,
            deniedTools ?? ["search"]);
    }

    private static RouteRuleViewModel CreateRouteRuleViewModel(string id, string ruleName, int priority)
    {
        var now = new DateTime(2026, 6, 8, 14, 0, 0, DateTimeKind.Utc);
        return new RouteRuleViewModel(
            id,
            "version-002",
            "writer",
            "support_agent",
            ruleName,
            priority,
            "intent",
            "{\"intent\":\"support\"}",
            "delegate_and_wait",
            "{\"mode\":\"summary_only\"}",
            "{\"mode\":\"read_only\"}",
            "{\"inherit\":false}",
            "{\"sources\":[\"faq\"]}",
            true,
            true,
            now,
            now);
    }

    private sealed class FakeMediator : IMediator
    {
        private readonly Func<object, object?> _handler;

        public FakeMediator(Func<GetAgentDefinitionsQuery, IReadOnlyList<AgentDefinitionViewModel>> handler)
        {
            _handler = request => request is GetAgentDefinitionsQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetAgentDefinitionByCodeQuery, AgentDefinitionViewModel?> handler)
        {
            _handler = request => request is GetAgentDefinitionByCodeQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<CreateAgentDefinitionCommand, AgentDefinitionViewModel> handler)
        {
            _handler = request => request is CreateAgentDefinitionCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(
            Func<GetAgentDefinitionByCodeQuery, AgentDefinitionViewModel?> getByCodeHandler,
            Func<GetAgentDefinitionVersionsQuery, IReadOnlyList<AgentDefinitionVersionViewModel>> getVersionsHandler)
        {
            _handler = request => request switch
            {
                GetAgentDefinitionByCodeQuery query => getByCodeHandler(query),
                GetAgentDefinitionVersionsQuery query => getVersionsHandler(query),
                _ => throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}")
            };
        }

        public FakeMediator(Func<CreateAgentDefinitionVersionCommand, AgentDefinitionVersionViewModel> handler)
        {
            _handler = request => request is CreateAgentDefinitionVersionCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<ActivateAgentDefinitionVersionCommand, AgentDefinitionViewModel> handler)
        {
            _handler = request => request is ActivateAgentDefinitionVersionCommand command
                ? handler(command)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<GetRouteRulesQuery, IReadOnlyList<RouteRuleViewModel>> handler)
        {
            _handler = request => request is GetRouteRulesQuery query
                ? handler(query)
                : throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public FakeMediator(Func<CreateRouteRuleCommand, RouteRuleViewModel> handler)
        {
            _handler = request => request is CreateRouteRuleCommand command
                ? handler(command)
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
