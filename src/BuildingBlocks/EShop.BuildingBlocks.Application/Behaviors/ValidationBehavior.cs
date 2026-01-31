using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Pipeline behavior for validating requests using FluentValidation
/// Works only with commands that return Result<T>
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
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
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .Where(r => !r.IsValid)
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count > 0)
        {
            var requestName = typeof(TRequest).Name;
            var errors = failures
                .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
                .ToDictionary(g => g.Key, g => g.ToArray());

            _logger.LogWarning(
                "Validation failed for {RequestName}. Errors: {@ValidationErrors}",
                requestName,
                errors);

            // Check if TResponse is Result<T> - if yes, return failure, if no - throw exception
            var resultType = typeof(TResponse);

            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Result<>))
            {
                var errorMessage = string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));
                var error = new Error("Validation.Failed", errorMessage);

                // Create Result<T>.Failure using reflection
                var failureMethod = resultType.GetMethod("Failure", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (failureMethod != null)
                {
                    return (TResponse)failureMethod.Invoke(null, [error])!;
                }
            }

            // Fallback: throw exception for non-Result responses
            throw new Exceptions.ValidationException(failures);
        }

        return await next();
    }
}
