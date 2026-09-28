using FluentValidation;
using MediatR;

namespace ECommerce.Application.Common.Behaviors;

/// <summary>
/// Runs the registered validators before the handler. Failures are thrown as a ValidationException,
/// FluentValidations own MediatR idiom, which hands them to GlobalExceptionHandler and lets the API
/// render one ProblemDetails extension per invalid field.
/// </summary>
/// <remarks>
/// The previous implementation built a Result failure by reflecting over a static Failure method
/// and joined every message into a single string, so a client could not tell which field was
/// rejected.
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
