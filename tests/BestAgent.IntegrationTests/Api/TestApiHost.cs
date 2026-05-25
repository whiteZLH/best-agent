using BestAgent.Api.Controllers;
using BestAgent.Application;
using BestAgent.Application.Abstractions;
using BestAgent.Application.Common;
using BestAgent.Infrastructure;
using BestAgent.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BestAgent.IntegrationTests.Api;

internal sealed class TestApiHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestApiHost(WebApplication app)
    {
        _app = app;
    }

    public HttpClient Client => _app.GetTestClient();

    public IServiceProvider Services => _app.Services;

    public static async Task<TestApiHost> CreateAsync(IEnumerable<Func<BestAgent.Application.Planning.PlanDecision>> responses)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "InMemory",
            ["Persistence:DatabaseName"] = Guid.NewGuid().ToString("N"),
            ["OpenAI:BaseUrl"] = "https://example.com/v1",
            ["OpenAI:ApiKey"] = "test-key",
            ["OpenAI:Model"] = "test-model",
            ["OpenAI:TimeoutSeconds"] = "60"
        });

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.RemoveAll(typeof(IModelGateway));
        builder.Services.AddSingleton<IModelGateway>(_ => new SequenceModelGateway(responses));
        builder.Services.AddControllers().AddApplicationPart(typeof(AgentRunsController).Assembly);
        builder.Services.AddProblemDetails();

        var app = builder.Build();

        app.UseExceptionHandler(exceptionApplication =>
        {
            exceptionApplication.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
                var (statusCode, title, errors) = exception switch
                {
                    ApplicationValidationException validationException => (StatusCodes.Status400BadRequest, "Validation error", validationException.Errors),
                    EntityNotFoundException => (StatusCodes.Status404NotFound, "Resource not found", Array.Empty<string>()),
                    InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid operation", Array.Empty<string>()),
                    _ => (StatusCodes.Status500InternalServerError, "Server error", Array.Empty<string>())
                };

                context.Response.StatusCode = statusCode;
                await Results.Problem(
                    statusCode: statusCode,
                    title: title,
                    detail: exception?.Message,
                    extensions: errors.Count > 0 ? new Dictionary<string, object?> { ["errors"] = errors } : null)
                    .ExecuteAsync(context);
            });
        });

        app.MapControllers();
        await app.Services.EnsureDatabaseInitializedAsync();
        await app.StartAsync();

        return new TestApiHost(app);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }
}
