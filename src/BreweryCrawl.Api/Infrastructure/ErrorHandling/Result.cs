namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

/// <summary>
/// Either a value or an <see cref="Error"/>. Used for expected failures (not found, business rules, ...)
/// so they are visible in method signatures instead of hidden as exceptions.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value) => _value = value;
    private Result(Error error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result.");

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onError) =>
        IsSuccess ? onSuccess(_value!) : onError(Error!);
}
