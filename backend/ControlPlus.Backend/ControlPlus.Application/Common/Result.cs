namespace ControlPlus.Application.Common;

/// <summary>
/// Represents the outcome of a use case without coupling the application layer to HTTP exceptions.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, ApplicationError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public ApplicationError? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(ApplicationError error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(ApplicationError error) => Result<T>.Failure(error);
}

public sealed class Result<T> : Result
{
    private Result(T? value, bool isSuccess, ApplicationError? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(value, true, null);

    public static new Result<T> Failure(ApplicationError error) =>
        new(default, false, error ?? throw new ArgumentNullException(nameof(error)));
}
