using BestAgent.Application.Abstractions;
using BestAgent.Application.Common;
using MediatR;

namespace BestAgent.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IRequestValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var errors = _validators
            .SelectMany(validator => validator.Validate(request))
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct()
            .ToArray();

        if (errors.Length > 0)
        {
            throw new ApplicationValidationException(errors);
        }

        return next();
    }
}
