using BestAgent.Application;
using BestAgent.Application.Common;
using BestAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler(exceptionApplication =>
{
    exceptionApplication.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>()?.Error;
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

app.MapHealthChecks("/health");
app.MapControllers();

await app.Services.EnsureDatabaseInitializedAsync();

app.Run();

public partial class Program;
