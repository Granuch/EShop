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

    /// <summary>
    /// Pattern matching for Result - executes one of two functions based on success/failure
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(Value!) : onFailure(Error!);
    }

    /// <summary>
    /// Executes an action based on success/failure
    /// </summary>
    public void Switch(Action<T> onSuccess, Action<Error> onFailure)
    {
        if (IsSuccess)
            onSuccess(Value!);
        else
            onFailure(Error!);
    }

    /// <summary>
    /// Implicit conversion from value to successful Result
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Implicit conversion from Error to failed Result
    /// </summary>
    public static implicit operator Result<T>(Error error) => Failure(error);

    public override string ToString()
    {
        return IsSuccess
            ? $"Success<{typeof(T).Name}>({Value})"
            : $"Failure<{typeof(T).Name}>({Error?.Code}: {Error?.Message})";
    }
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
        if (isSuccess && error != null)
            throw new InvalidOperationException("Success result cannot have an error");

        if (!isSuccess && error == null)
            throw new InvalidOperationException("Failure result must have an error");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);

    /// <summary>
    /// Pattern matching for Result - executes one of two functions based on success/failure
    /// </summary>
    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        return IsSuccess ? onSuccess() : onFailure(Error!);
    }

    /// <summary>
    /// Executes an action based on success/failure
    /// </summary>
    public void Switch(Action onSuccess, Action<Error> onFailure)
    {
        if (IsSuccess)
            onSuccess();
        else
            onFailure(Error!);
    }

    /// <summary>
    /// Implicit conversion from Error to failed Result
    /// </summary>
    public static implicit operator Result(Error error) => Failure(error);

    public override string ToString()
    {
        return IsSuccess
            ? "Success"
            : $"Failure({Error?.Code}: {Error?.Message})";
    }
}
