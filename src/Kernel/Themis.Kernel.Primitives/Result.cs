namespace Themis.Kernel.Primitives;

/// <summary>The outcome of a command or a check: success, or one or more <see cref="Error"/> values (R-API-03).</summary>
public class Result
{
    private static readonly Result SuccessResult = new([]);

    protected Result(IReadOnlyList<Error> errors)
    {
        Errors = errors;
    }

    public IReadOnlyList<Error> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    public bool IsFailure => !IsSuccess;

    /// <summary>The first error, which decides the HTTP status and the ruleId reported (R-API-03).</summary>
    public Error? Primary => Errors.Count == 0 ? null : Errors[0];

    public static Result Success() => SuccessResult;

    public static Result Failure(Error error) => new([error]);

    public static Result Failure(IEnumerable<Error> errors)
    {
        List<Error> list = [.. errors];
        return list.Count == 0 ? throw new ArgumentException("A failure needs at least one error.", nameof(errors)) : new Result(list);
    }

    public static implicit operator Result(Error error) => Failure(error);

    public static Result FromError(Error error) => Failure(error);
}

/// <summary>A <see cref="Result"/> that carries a value on success.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Success and Failure factories read naturally as Result<T>.Success(value).")]
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value)
        : base([])
    {
        _value = value;
    }

    private Result(IReadOnlyList<Error> errors)
        : base(errors)
    {
    }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"The result failed: {Primary}.");

    public static Result<T> Success(T value) => new(value);

    public static new Result<T> Failure(Error error) => new([error]);

    public static new Result<T> Failure(IEnumerable<Error> errors)
    {
        List<Error> list = [.. errors];
        return list.Count == 0 ? throw new ArgumentException("A failure needs at least one error.", nameof(errors)) : new Result<T>(list);
    }

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
