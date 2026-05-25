using MediatR;
using Microsoft.Extensions.Logging;

namespace BestAgent.Application.Behaviors;

public sealed class RequestLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<RequestLoggingBehavior<TRequest, TResponse>> _logger;

    public RequestLoggingBehavior(ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling request {RequestType}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled request {RequestType}", typeof(TRequest).Name);
        return response;
    }
}
