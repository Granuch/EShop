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

    // TODO: Add integration with FluentValidation.Results.ValidationResult
}
