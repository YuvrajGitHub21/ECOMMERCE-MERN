using System.Collections.Concurrent;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryEasy.Application.Messaging;

/// <summary>
/// Routes a request to its handler.
/// </summary>
/// <remarks>
/// <para>
/// The awkward part this type exists to solve: <see cref="Send{TResponse}"/> knows the
/// response type statically but only ever sees the command as <see cref="ICommand{TResponse}"/>.
/// Resolving <c>ICommandHandler&lt;RegisterCommand, TokenPair&gt;</c> needs the concrete
/// command type, which is a runtime value. So each request type is closed over once through
/// reflection, and the resulting wrapper is cached forever.
/// </para>
/// <para>
/// After the first request of a given type there is no reflection left on the path: a
/// dictionary lookup and a virtual call. The cache is static because the closed wrapper types
/// depend only on the request type, never on the container.
/// </para>
/// </remarks>
internal sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> _commandWrappers = new();
    private static readonly ConcurrentDictionary<Type, object> _queryWrappers = new();

    // Static factory delegates, invoked only on a cache miss. Taking the response type as an
    // explicit argument keeps these lambdas non-capturing, so the hot path allocates nothing.
    private static readonly Func<Type, Type, object> _commandWrapperFactory =
        static (commandType, responseType) => Activator.CreateInstance(
            typeof(CommandHandlerWrapper<,>).MakeGenericType(commandType, responseType))!;

    private static readonly Func<Type, Type, object> _queryWrapperFactory =
        static (queryType, responseType) => Activator.CreateInstance(
            typeof(QueryHandlerWrapper<,>).MakeGenericType(queryType, responseType))!;

    /// <inheritdoc />
    public Task<Result<TResponse>> Send<TResponse>(
        ICommand<TResponse> command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (CommandHandlerWrapper<TResponse>)_commandWrappers.GetOrAdd(
            command.GetType(),
            _commandWrapperFactory,
            typeof(TResponse));

        return wrapper.Handle(command, serviceProvider, ct);
    }

    /// <inheritdoc />
    public Task<Result<TResponse>> Query<TResponse>(
        IQuery<TResponse> query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = (QueryHandlerWrapper<TResponse>)_queryWrappers.GetOrAdd(
            query.GetType(),
            _queryWrapperFactory,
            typeof(TResponse));

        return wrapper.Handle(query, serviceProvider, ct);
    }
}

/// <summary>
/// Lets the dispatcher call a handler whose command type it does not know statically.
/// </summary>
internal abstract class CommandHandlerWrapper<TResponse>
{
    public abstract Task<Result<TResponse>> Handle(
        ICommand<TResponse> command,
        IServiceProvider serviceProvider,
        CancellationToken ct);
}

internal sealed class CommandHandlerWrapper<TCommand, TResponse> : CommandHandlerWrapper<TResponse>
    where TCommand : ICommand<TResponse>
{
    public override Task<Result<TResponse>> Handle(
        ICommand<TResponse> command,
        IServiceProvider serviceProvider,
        CancellationToken ct)
    {
        // Resolved per request rather than held, so the decorator chain is rebuilt against the
        // current scope. GetRequiredService, not GetService: a command with no registered
        // handler is a wiring bug that should surface loudly at the first call, not as a null.
        ICommandHandler<TCommand, TResponse> handler =
            serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();

        return handler.Handle((TCommand)command, ct);
    }
}

/// <inheritdoc cref="CommandHandlerWrapper{TResponse}" />
internal abstract class QueryHandlerWrapper<TResponse>
{
    public abstract Task<Result<TResponse>> Handle(
        IQuery<TResponse> query,
        IServiceProvider serviceProvider,
        CancellationToken ct);
}

internal sealed class QueryHandlerWrapper<TQuery, TResponse> : QueryHandlerWrapper<TResponse>
    where TQuery : IQuery<TResponse>
{
    public override Task<Result<TResponse>> Handle(
        IQuery<TResponse> query,
        IServiceProvider serviceProvider,
        CancellationToken ct)
    {
        IQueryHandler<TQuery, TResponse> handler =
            serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();

        return handler.Handle((TQuery)query, ct);
    }
}
