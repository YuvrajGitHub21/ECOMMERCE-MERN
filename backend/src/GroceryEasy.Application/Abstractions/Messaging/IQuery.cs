namespace GroceryEasy.Application.Abstractions.Messaging;

/// <summary>
/// A request that reads state and returns <typeparamref name="TResponse"/>.
/// </summary>
/// <remarks>
/// Queries never open a transaction. A read that needed one would be a sign it is really a
/// command, or that it is doing read-modify-write in application code — which is the exact
/// shape of legacy defect L-04.
/// </remarks>
/// <typeparam name="TResponse">What the query returns.</typeparam>
public interface IQuery<TResponse>;
