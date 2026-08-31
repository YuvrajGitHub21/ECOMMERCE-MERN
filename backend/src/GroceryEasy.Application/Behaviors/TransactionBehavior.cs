using GroceryEasy.Application.Abstractions.Data;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace GroceryEasy.Application.Behaviors;

/// <summary>
/// Runs a command inside a database transaction. Innermost decorator, and commands only.
/// </summary>
/// <remarks>
/// <para>
/// Commits when the handler returns success and rolls back on any failure, so a command that
/// writes three tables and then decides the request is invalid leaves nothing behind. This is
/// the machinery that makes "reserve stock, book the slot, create the order, write the outbox
/// row — all or nothing" expressible at all, and it is the direct structural answer to legacy
/// defects L-04 and L-15, where a partial write was simply the normal outcome.
/// </para>
/// <para>
/// Innermost on purpose. Validation sits outside it, so an invalid command never opens a
/// transaction; logging sits outside both, so the log line covers the commit as well as the
/// handler.
/// </para>
/// <para>
/// Queries are deliberately not decorated. A read that needs a transaction is either really a
/// command or is doing read-modify-write in application code.
/// </para>
/// </remarks>
internal sealed class CommandTransactionBehavior<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IApplicationDbContext dbContext)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
    {
        // Already inside one — join it rather than opening a nested transaction, which EF Core
        // rejects. The outermost command owns the commit.
        if (dbContext.HasActiveTransaction)
        {
            return await inner.Handle(command, ct);
        }

        await using IDbContextTransaction transaction = await dbContext.BeginTransactionAsync(ct);

        // Not wrapped in try/catch: if the handler throws, DisposeAsync rolls back, and the
        // exception continues to the global handler. Swallowing it here would turn a bug into
        // a silent 500 with no stack trace, which is how the legacy app lost its failures.
        Result<TResponse> result = await inner.Handle(command, ct);

        if (result.IsSuccess)
        {
            await transaction.CommitAsync(ct);
        }
        else
        {
            await transaction.RollbackAsync(ct);
        }

        return result;
    }
}
