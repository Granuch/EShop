namespace EShop.BuildingBlocks.Application;

/// <summary>
/// Result pattern for handling operation outcomes
/// </summary>
public class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public Error? Error { get; }

    private Result(bool isSuccess, T? value, Error? error)
    {
        if (isSuccess && error != null)
            throw new InvalidOperationException("Success result cannot have an error");

        if (!isSuccess && error == null)
            throw new InvalidOperationException("Failure result must have an error");

        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);

    // TODO: Add implicit conversion operators for easier usage
    // TODO: Add Match method for functional approach
    // public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
}

/// <summary>
/// Result pattern without value for void operations
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
}
