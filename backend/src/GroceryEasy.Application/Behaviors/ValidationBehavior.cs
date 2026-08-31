using FluentValidation;
using FluentValidation.Results;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Behaviors;

/// <summary>
/// The shared body of the validation decorators.
/// </summary>
/// <remarks>
/// <para>
/// <b>This does not throw.</b> FluentValidation's own <c>ValidateAndThrow</c> and MediatR's
/// usual validation pipeline both raise a <c>ValidationException</c> that something upstream
/// catches and translates. That makes a rejected password confirmation an exceptional control
/// path — it costs a stack capture on a route users hit constantly, and it unwinds straight
/// through the transaction decorator. Here a validation failure is what it actually is: an
/// expected <see cref="Result"/> failure, returned normally.
/// </para>
/// <para>
/// When validation fails the inner handler is never invoked. An integration test asserts that
/// directly, because "returns 400" and "returns 400 without touching the database" are
/// different guarantees.
/// </para>
/// </remarks>
internal static class ValidationBehavior
{
    /// <summary>
    /// Runs every registered validator for <paramref name="request"/>.
    /// </summary>
    /// <returns>
    /// The collected failures, or <see langword="null"/> when the request is valid.
    /// </returns>
    public static async Task<ValidationError?> ValidateAsync<TRequest>(
        TRequest request,
        IEnumerable<IValidator<TRequest>> validators,
        CancellationToken ct)
    {
        // Materialised because the common case is zero or one validator, and enumerating an
        // empty collection should not allocate a context.
        IValidator<TRequest>[] applicable = validators as IValidator<TRequest>[] ?? [.. validators];

        if (applicable.Length == 0)
        {
            return null;
        }

        var context = new ValidationContext<TRequest>(request);

        ValidationResult[] results = await Task.WhenAll(
            applicable.Select(validator => validator.ValidateAsync(context, ct)));

        Error[] failures = [.. results
            .Where(result => !result.IsValid)
            .SelectMany(result => result.Errors)
            .Select(failure => Error.Validation(failure.PropertyName, failure.ErrorMessage))];

        return failures.Length == 0 ? null : new ValidationError(failures);
    }
}

/// <summary>Validates a command before its handler runs.</summary>
internal sealed class CommandValidationBehavior<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
    {
        ValidationError? failure = await ValidationBehavior.ValidateAsync(command, validators, ct);

        return failure is not null
            ? Result<TResponse>.Failure(failure)
            : await inner.Handle(command, ct);
    }
}

/// <summary>Validates a query before its handler runs.</summary>
internal sealed class QueryValidationBehavior<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    IEnumerable<IValidator<TQuery>> validators)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct)
    {
        ValidationError? failure = await ValidationBehavior.ValidateAsync(query, validators, ct);

        return failure is not null
            ? Result<TResponse>.Failure(failure)
            : await inner.Handle(query, ct);
    }
}
