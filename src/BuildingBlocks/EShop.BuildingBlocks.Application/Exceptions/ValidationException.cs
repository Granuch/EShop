using FluentValidation.Results;

namespace EShop.BuildingBlocks.Application.Exceptions;

/// <summary>
/// Exception thrown when validation fails
/// </summary>
public class ValidationException : Exception
{
    public Dictionary<string, string[]> Errors { get; }

    public ValidationException() 
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<KeyValuePair<string, string[]>> failures)
        : this()
    {
        Errors = failures.ToDictionary(k => k.Key, v => v.Value);
    }

    /// <summary>Keys are the camelCase wire names, as on the Result path (<see cref="FieldValidationError.KeyFor"/>).</summary>
    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = FieldValidationError.ToErrorMap(failures);
    }
}
