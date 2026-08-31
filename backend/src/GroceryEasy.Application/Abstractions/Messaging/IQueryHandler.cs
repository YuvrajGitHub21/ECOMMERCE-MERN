using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Abstractions.Messaging;

/// <summary>
/// Handles exactly one <typeparamref name="TQuery"/>.
/// </summary>
/// <inheritdoc cref="ICommandHandler{TCommand,TResponse}" path="/remarks"/>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    /// <summary>Executes the query.</summary>
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct);
}
