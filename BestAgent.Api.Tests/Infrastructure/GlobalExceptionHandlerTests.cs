using System.Text.Json;
using System.Text.Json.Serialization;
using BestAgent.Api.Infrastructure;
using BestAgent.Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ShouldMapNotFoundExceptionTo404()
    {
        var environment = CreateEnvironment(isDevelopment: true);
        var handler = new GlobalExceptionHandler(environment);
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new NotFoundException("AgentRun", "run-001"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Not Found", problem.Title);
        Assert.Contains("run-001", problem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldMapConflictExceptionTo409()
    {
        var environment = CreateEnvironment(isDevelopment: true);
        var handler = new GlobalExceptionHandler(environment);
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new ConflictException("conflict happened"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Equal("conflict happened", problem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldMapForbiddenExceptionTo403()
    {
        var environment = CreateEnvironment(isDevelopment: true);
        var handler = new GlobalExceptionHandler(environment);
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new ForbiddenException("approval forbidden"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(403, problem.Status);
        Assert.Equal("Forbidden", problem.Title);
        Assert.Equal("approval forbidden", problem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldMapUnauthorizedExceptionTo401()
    {
        var environment = CreateEnvironment(isDevelopment: true);
        var handler = new GlobalExceptionHandler(environment);
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new UnauthorizedException("invalid webhook signature"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(401, problem.Status);
        Assert.Equal("Unauthorized", problem.Title);
        Assert.Equal("invalid webhook signature", problem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldMapInvalidOperationExceptionTo422()
    {
        var environment = CreateEnvironment(isDevelopment: true);
        var handler = new GlobalExceptionHandler(environment);
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("bad input"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, httpContext.Response.StatusCode);
        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(422, problem.Status);
        Assert.Equal("Unprocessable Entity", problem.Title);
        Assert.Equal("bad input", problem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldHideInternalErrorDetailOutsideDevelopment()
    {
        var environment = CreateEnvironment(isDevelopment: false);
        var handler = new GlobalExceptionHandler(environment);
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new Exception("top secret"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(500, problem.Status);
        Assert.Equal("Internal Server Error", problem.Title);
        Assert.Equal("An unexpected error occurred.", problem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldExposeInternalErrorDetailInDevelopment()
    {
        var environment = CreateEnvironment(isDevelopment: true);
        var handler = new GlobalExceptionHandler(environment);
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new Exception("top secret"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(500, problem.Status);
        Assert.Equal("Internal Server Error", problem.Title);
        Assert.Equal("top secret", problem.Detail);
    }

    private static IHostEnvironment CreateEnvironment(bool isDevelopment)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(isDevelopment ? Environments.Development : Environments.Production);
        return environment;
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        return new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
    }

    private static async Task<ProblemDetailsResponse> ReadProblemDetailsAsync(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<ProblemDetailsResponse>(httpContext.Response.Body);
        return Assert.IsType<ProblemDetailsResponse>(payload);
    }

    private sealed record ProblemDetailsResponse(
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("detail")] string Detail);
}
