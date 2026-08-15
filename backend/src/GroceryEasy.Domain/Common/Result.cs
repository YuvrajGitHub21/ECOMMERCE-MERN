using System.Diagnostics.CodeAnalysis;

namespace GroceryEasy.Domain.Common;

/// <summary>
/// The outcome of an operation that is expected to be able to fail.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule that decides between this and an exception:</b> a <see cref="Result"/> is for
/// failures the caller was always going to have to handle — the email is already taken, the
/// refresh token was revoked, the reset link expired. An exception is for a bug or a broken
/// environment — a null argument, an unreachable database, a missing configuration value.
/// </para>
/// <para>
/// Exceptions are never used for control flow in a handler. Two reasons, both practical: a
/// throw unwinds through the transaction behaviour, so an expected failure would roll back
/// work that should have been committed; and an expected failure is not exceptional, so it
/// should not cost a stack capture on a path the system takes routinely.
/// </para>
/// </remarks>
public class Result
{
    /// <summary>
    /// Initialises a result, rejecting the two states that must not exist: a success that
    /// carries an error, and a failure that does not.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The success flag and the error disagree. That is a programming mistake in the
    /// factory being called, not an expected failure, so it throws.
    /// </exception>
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("A failed result must carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether the operation failed. The inverse of <see cref="IsSuccess"/>.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The failure, or <see cref="Common.Error.None"/> when <see cref="IsSuccess"/> is true.
    /// </summary>
    public Error Error { get; }

    /// <summary>Creates a successful result carrying no value.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Creates a failed result.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Creates a failed result of the given value type.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// The outcome of an operation that returns a value when it succeeds.
/// </summary>
/// <typeparam name="TValue">The type produced on success.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>
    /// The value produced by a successful operation.
    /// </summary>
    /// <remarks>
    /// Reading this on a failed result throws rather than returning <see langword="null"/> or a
    /// default. Doing so means the caller skipped its <see cref="Result.IsSuccess"/> check, which
    /// is a bug in the caller — and a loud one here is far cheaper than a null that travels three
    /// layers before dereferencing somewhere unrelated. The legacy system's habit of carrying a
    /// null user object forward until something exploded is exactly what this prevents.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"The value of a failed result cannot be accessed. Error: {Error.Code}");

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <remarks>
    /// Declared here rather than inherited so that <c>Result&lt;User&gt;.Success(user)</c> and
    /// <c>Result&lt;User&gt;.Failure(error)</c> both read the same way and both return
    /// <see cref="Result{TValue}"/>. The inherited <see cref="Result.Failure{TValue}"/> cannot
    /// infer <typeparamref name="TValue"/> from an <see cref="Common.Error"/> argument, so
    /// without this declaration the call would silently bind to the non-generic
    /// <see cref="Result.Failure(Common.Error)"/> and return the wrong type.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1000:Do not declare static members on generic types",
        Justification =
            "CA1000 guards against callers having to name the type argument explicitly. Here the " +
            "static factories are the only way to construct the type — the constructor is " +
            "internal — and the type argument is inferred at every real call site or supplied by " +
            "the method's own return type. This is the standard shape for a Result type.")]
    public static Result<TValue> Success(TValue value) => new(value, true, Error.None);

    /// <summary>Creates a failed result of this value type.</summary>
    [SuppressMessage(
        "Design",
        "CA1000:Do not declare static members on generic types",
        Justification = "See the justification on Success(TValue).")]
    public static new Result<TValue> Failure(Error error) => new(default, false, error);

    /// <summary>
    /// Lets a handler <c>return someValue;</c> where a <see cref="Result{TValue}"/> is expected,
    /// instead of wrapping every success in a factory call.
    /// </summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
