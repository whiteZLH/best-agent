using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Knowledge;
using BestAgent.Domain.Tools;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Infrastructure.Model;
using BestAgent.Infrastructure.Observability;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using BestAgent.Infrastructure.Persistence.Seeding;
using BestAgent.Infrastructure.Runtime;
using BestAgent.Infrastructure.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BestAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=best_agent;Username=postgres;Password=postgres";
        var openAiOptions = new OpenAiOptions
        {
            BaseUrl = configuration["OpenAI:BaseUrl"] ?? string.Empty,
            ApiKey = configuration["OpenAI:ApiKey"] ?? string.Empty,
            Model = configuration["OpenAI:Model"] ?? string.Empty,
            Temperature = decimal.TryParse(configuration["OpenAI:Temperature"], out var openAiTemperature)
                ? openAiTemperature
                : 0.2m,
            TimeoutSeconds = int.TryParse(configuration["OpenAI:TimeoutSeconds"], out var timeoutSeconds)
                ? timeoutSeconds
                : 60
        };
        var approvalTimeoutOptions = new ApprovalTimeoutOptions
        {
            TimeoutMinutes = int.TryParse(configuration["Approval:TimeoutMinutes"], out var approvalTimeoutMinutes)
                ? approvalTimeoutMinutes
                : 30,
            PollIntervalSeconds = int.TryParse(configuration["Approval:PollIntervalSeconds"], out var pollIntervalSeconds)
                ? pollIntervalSeconds
                : 5,
            BatchSize = int.TryParse(configuration["Approval:BatchSize"], out var approvalBatchSize)
                ? approvalBatchSize
                : 100,
            TimeoutComment = string.IsNullOrWhiteSpace(configuration["Approval:TimeoutComment"])
                ? "Approval timed out."
                : configuration["Approval:TimeoutComment"]!.Trim(),
            TimeoutAction = string.IsNullOrWhiteSpace(configuration["Approval:TimeoutAction"])
                ? ApprovalTimeoutOptions.RejectAction
                : configuration["Approval:TimeoutAction"]!.Trim()
        };
        var runOutboxPublisherOptions = new RunOutboxPublisherOptions
        {
            EndpointUrl = configuration["Outbox:Publisher:EndpointUrl"],
            AuthorizationHeader = configuration["Outbox:Publisher:AuthorizationHeader"],
            TimeoutSeconds = int.TryParse(configuration["Outbox:Publisher:TimeoutSeconds"], out var outboxTimeoutSeconds)
                ? outboxTimeoutSeconds
                : 5
        };
        var runOutboxDispatcherOptions = new RunOutboxDispatcherOptions
        {
            BatchSize = int.TryParse(configuration["Outbox:Dispatcher:BatchSize"], out var outboxBatchSize)
                ? outboxBatchSize
                : 100,
            PollIntervalSeconds = int.TryParse(configuration["Outbox:Dispatcher:PollIntervalSeconds"], out var outboxPollIntervalSeconds)
                ? outboxPollIntervalSeconds
                : 2,
            MaxRetryCount = int.TryParse(configuration["Outbox:Dispatcher:MaxRetryCount"], out var outboxMaxRetryCount)
                ? outboxMaxRetryCount
                : 3
        };

        services.AddDbContext<BestAgentDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(openAiOptions);
        services.AddSingleton(approvalTimeoutOptions);
        services.AddSingleton(runOutboxPublisherOptions);
        services.AddSingleton(runOutboxDispatcherOptions);
        services.AddSingleton<BestAgent.Application.Observability.IAgentMetrics, AgentMetrics>();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<OpenAiOptions>();
            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 60)
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                httpClient.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
            }

            return httpClient;
        });
        services.AddSingleton<IModelGateway, OpenAiCompatibleModelGateway>();
        services.AddHttpClient("ToolWebhook");
        services.AddHttpClient("RunOutboxPublisher", (sp, client) =>
        {
            var options = sp.GetRequiredService<RunOutboxPublisherOptions>();
            var timeoutSeconds = options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 5;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
        services.AddSingleton<InMemoryToolHandlerRegistry>();
        services.AddSingleton<IToolHandlerRegistry>(sp => sp.GetRequiredService<InMemoryToolHandlerRegistry>());
        services.AddScoped<IToolResolver, ToolResolver>();
        services.AddSingleton<IToolInputValidator, JsonSchemaToolInputValidator>();
        services.AddSingleton<IToolOutputValidator, JsonSchemaToolOutputValidator>();
        services.AddSingleton<IAgentOutputValidator, JsonSchemaAgentOutputValidator>();
        services.AddScoped<IHttpToolInvoker, HttpToolInvoker>();
        services.AddScoped<IToolExecutor, ToolExecutor>();
        services.AddScoped<IAgentDefinitionRepository, AgentDefinitionRepository>();
        services.AddScoped<IRouteRuleRepository, RouteRuleRepository>();
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAgentStepRepository, AgentStepRepository>();
        services.AddScoped<IAgentApprovalRepository, AgentApprovalRepository>();
        services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();
        services.AddScoped<IRunOutboxEventRepository, RunOutboxEventRepository>();
        services.AddScoped<IToolDefinitionRepository, ToolDefinitionRepository>();
        services.AddScoped<IToolInvocationRepository, ToolInvocationRepository>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IKnowledgeChunkRepository, KnowledgeChunkRepository>();
        services.AddScoped<IEmbeddingIndexRepository, EmbeddingIndexRepository>();
        services.AddScoped<ISessionMemoryRepository, SessionMemoryRepository>();
        services.AddScoped<IUserMemoryRepository, UserMemoryRepository>();
        services.AddScoped<ISummaryMemoryRepository, SummaryMemoryRepository>();
        services.AddScoped<RuntimeContextComposer>();
        services.AddScoped<RuntimeMemoryWriter>();
        services.AddScoped<IRuntimeContextComposer>(sp => sp.GetRequiredService<RuntimeContextComposer>());
        services.AddScoped<IRuntimeMemoryWriter>(sp => sp.GetRequiredService<RuntimeMemoryWriter>());
        services.AddSingleton<IRunOutboxEventPublisher, HttpRunOutboxEventPublisher>();
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddHostedService<AgentRunWorker>();
        services.AddHostedService<ApprovalTimeoutDispatcher>();
        services.AddHostedService<RunOutboxEventDispatcher>();

        return services;
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }
}
