using FluentValidation;
using MediatR;

namespace EShop.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Pipeline behavior for validating requests using FluentValidation
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
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

        // TODO: Execute all validators in parallel
        // TODO: Aggregate validation errors
        // TODO: Throw ValidationException if any validation fails
        // var context = new ValidationContext<TRequest>(request);
        // var validationResults = await Task.WhenAll(
        //     _validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        
        // var failures = validationResults
        //     .Where(r => !r.IsValid)
        //     .SelectMany(r => r.Errors)
        //     .ToList();

        // if (failures.Any())
        //     throw new ValidationException(failures);

        return await next();
    }
}
