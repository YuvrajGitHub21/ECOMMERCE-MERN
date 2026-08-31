using System.Diagnostics;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GroceryEasy.Application.Behaviors;

/// <summary>
/// The shared body of the logging decorators.
/// </summary>
/// <remarks>
/// <para>
/// <b>Request bodies are never logged.</b> Not the command, not its properties, not the error
/// description. Commands in this phase carry plaintext passwords, reset tokens and email
/// verification tokens; a log line containing any of those is a credential leak into whatever
/// aggregates the logs, and it survives long after the token expires. Only the request type
/// name, the outcome and the elapsed time go out.
/// </para>
/// <para>
/// The error <em>code</em> is logged but the description is not, because descriptions are
/// user-facing prose that can quote submitted values.
/// </para>
/// </remarks>
internal static class LoggingBehavior
{
    public static async Task<Result<TResponse>> RunAsync<TResponse>(
        ILogger logger,
        string requestKind,
        string requestName,
        Func<Task<Result<TResponse>>> next)
    {
        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            Result<TResponse> result = await next();
            double elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (result.IsSuccess)
            {
                // Guarded because the success path is the hot one and Information is commonly
                // switched off in production, where this would otherwise box the elapsed time
                // on every single request only to discard it.
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "{RequestKind} {RequestName} succeeded in {ElapsedMs:F1} ms",
                        requestKind,
                        requestName,
                        elapsedMs);
                }
            }
            else
            {
                // A failed Result is an expected outcome, not an incident: warning, not error.
                logger.LogWarning(
                    "{RequestKind} {RequestName} failed in {ElapsedMs:F1} ms with {ErrorCode} ({ErrorType})",
                    requestKind,
                    requestName,
                    elapsedMs,
                    result.Error.Code,
                    result.Error.Type);
            }

            return result;
        }
        catch (Exception exception)
        {
            // An exception here means a bug or a broken dependency, which is what Error level
            // is for. Rethrown so the global exception handler still owns the response.
            double elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            logger.LogError(
                exception,
                "{RequestKind} {RequestName} threw after {ElapsedMs:F1} ms",
                requestKind,
                requestName,
                elapsedMs);

            throw;
        }
    }
}

/// <summary>Logs the outcome and duration of every command. Outermost decorator.</summary>
internal sealed class CommandLoggingBehavior<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<CommandLoggingBehavior<TCommand, TResponse>> logger)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct) =>
        LoggingBehavior.RunAsync(
            logger,
            "Command",
            typeof(TCommand).Name,
            () => inner.Handle(command, ct));
}

/// <summary>Logs the outcome and duration of every query. Outermost decorator.</summary>
internal sealed class QueryLoggingBehavior<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    ILogger<QueryLoggingBehavior<TQuery, TResponse>> logger)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct) =>
        LoggingBehavior.RunAsync(
            logger,
            "Query",
            typeof(TQuery).Name,
            () => inner.Handle(query, ct));
}
