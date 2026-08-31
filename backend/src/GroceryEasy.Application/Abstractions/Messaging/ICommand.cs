namespace GroceryEasy.Application.Abstractions.Messaging;

/// <summary>
/// A request that changes state and returns <typeparamref name="TResponse"/> when it succeeds.
/// </summary>
/// <remarks>
/// Commands are wrapped in a database transaction by the pipeline; queries are not. That is
/// the only behavioural difference between this and <see cref="IQuery{TResponse}"/>, and it is
/// the reason they are separate interfaces rather than one marker with a boolean on it.
/// </remarks>
/// <typeparam name="TResponse">
/// What the command produces. Use <see cref="Unit"/> when it produces nothing.
/// </typeparam>
public interface ICommand<TResponse>;
