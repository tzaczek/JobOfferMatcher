namespace JobOfferMatcher.Domain.Common;

/// <summary>A void result payload, so <c>Result&lt;Unit&gt;</c> models a command with no return value.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}

/// <summary>
/// An expected (non-exceptional) failure: a stable machine-readable <see cref="Code"/> plus a
/// human message. Codes (e.g. <c>ScanInProgress</c>, <c>UnknownCurrency</c>) drive control flow
/// and map to HTTP problem responses at the Web boundary (contracts/rest-api.md).
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>
/// Railway-oriented result for the success/failure of a command without a return value
/// (Constitution Principle I — commands return <c>Result&lt;Unit&gt;</c>). Use <see cref="Result{T}"/>
/// when a value is produced. Reserve exceptions for genuinely exceptional conditions.
/// </summary>
public readonly struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    // Generic conveniences so call sites read `Result.Failure<OfferId>(...)`.
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Railway-oriented result carrying a value on success or an <see cref="Error"/> on failure.</summary>
public readonly struct Result<T>
{
    private readonly T? _value;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    /// <summary>The success value. Throws if accessed on a failed result — branch on <see cref="IsSuccess"/> first.</summary>
    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException($"Cannot access the value of a failed result ({Error}).");

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
        Error = Error.None;
    }

    private Result(Error error)
    {
        _value = default;
        IsSuccess = false;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>Fold both branches into a single value.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error);

    /// <summary>Transform the success value, propagating failure unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Failure(Error);

    /// <summary>Chain a result-producing step, short-circuiting on failure.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) =>
        IsSuccess ? bind(_value!) : Result<TOut>.Failure(Error);
}
