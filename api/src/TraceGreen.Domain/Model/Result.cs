namespace TraceGreen.Domain.Model;

/// <summary>Success-or-error value without exceptions for expected failures.</summary>
public sealed class Result<T, TError>
{
    private readonly T? _value;
    private readonly TError? _error;

    private Result(bool isOk, T? value, TError? error)
    {
        IsOk = isOk;
        _value = value;
        _error = error;
    }

    public bool IsOk { get; }

    public T Value => IsOk ? _value! : throw new InvalidOperationException("Result holds an error, not a value.");

    public TError Error => IsOk ? throw new InvalidOperationException("Result holds a value, not an error.") : _error!;

    public static Result<T, TError> Ok(T value) => new(true, value, default);

    public static Result<T, TError> Fail(TError error) => new(false, default, error);

    public TOut Match<TOut>(Func<T, TOut> ok, Func<TError, TOut> fail) => IsOk ? ok(_value!) : fail(_error!);
}
