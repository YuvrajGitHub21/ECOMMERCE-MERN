using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GroceryEasy.Api.Middleware;

/// <summary>
/// Turns any unhandled exception into RFC 9457 ProblemDetails.
/// </summary>
/// <remarks>
/// <para>
/// An unhandled exception reaching here is a <b>bug</b>, not an expected failure. Expected
/// failures — email already taken, invalid token, out of stock — travel as
/// <c>Result.Failure</c> and never become exceptions. That split is ADR-0005, and this handler is
/// the other half of it: the safety net, not the mechanism.
/// </para>
/// <para>
/// <b>Development returns the exception message and stack trace; Production returns neither.</b>
/// A stack trace tells an attacker the framework versions, the file layout and often the database
/// schema. What Production returns instead is the correlation identifier, which is enough for an
/// operator to find the full detail in the logs and useless to anybody else.
/// </para>
/// </remarks>
internal sealed class GlobalExceptionHandler(
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        string correlationId = httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}. Correlation id {CorrelationId}.",
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId);

        ProblemDetails problem = new()
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://groceryeasy.dev/errors/failure",
            Detail = environment.IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred. Quote the correlation id when reporting this.",
        };

        problem.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = problem.Status.Value;

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
