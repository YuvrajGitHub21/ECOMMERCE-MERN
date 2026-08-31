using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Abstractions.Messaging;

/// <summary>
/// The single entry point from the API layer into Application.
/// </summary>
/// <remarks>
/// Endpoints depend on this and nothing else from Application, so an endpoint cannot reach
/// past the pipeline to a handler. Both methods return a <see cref="Result{TValue}"/> rather
/// than throwing on failure, and the API layer maps that onto ProblemDetails in one place.
/// </remarks>
public interface IDispatcher
{
    /// <summary>Routes a command to its handler, through the full decorator pipeline.</summary>
    Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);

    /// <summary>Routes a query to its handler, through the full decorator pipeline.</summary>
    Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}
