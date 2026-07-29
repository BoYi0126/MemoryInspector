using System.Diagnostics.CodeAnalysis;

namespace MemoryInspector.Common;

/// <summary>
/// Represents the outcome of an operation that does not return a value.
/// </summary>
public class Result
{
    private protected Result(bool isSuccess, Error error)
    {
        Guard.NotNull(error);

        if (isSuccess && error.Code != ErrorCode.None)
        {
            throw new ArgumentException(
                "A successful result cannot contain an error.",
                nameof(error));
        }

        if (!isSuccess && error.Code == ErrorCode.None)
        {
            throw new ArgumentException(
                "A failed result must contain an error.",
                nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        Guard.NotNull(onSuccess);
        Guard.NotNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(Error);
    }
}

/// <summary>
/// Represents the outcome of an operation that returns a value on success.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            "A failed result does not contain a value.");

    public static Result<T> Success(T value) => new(true, value, Error.None);

    public new static Result<T> Failure(Error error) => new(false, default, error);

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsSuccess;
    }

    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        Guard.NotNull(onSuccess);
        Guard.NotNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }
}
