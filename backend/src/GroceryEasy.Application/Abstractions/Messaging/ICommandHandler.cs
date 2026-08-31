using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Abstractions.Messaging;

/// <summary>
/// Handles exactly one <typeparamref name="TCommand"/>.
/// </summary>
/// <remarks>
/// Implementations are <c>sealed</c> and <c>internal</c> — an architecture test enforces
/// both. Internal because nothing outside Application should reach a handler directly; the
/// only supported entry point is <see cref="IDispatcher"/>, which is what guarantees the
/// logging, validation and transaction decorators actually run. A handler resolved directly
/// from the container would silently bypass all three.
/// </remarks>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>Executes the command.</summary>
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}
