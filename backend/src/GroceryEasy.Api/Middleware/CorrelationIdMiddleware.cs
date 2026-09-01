using Serilog.Context;

namespace GroceryEasy.Api.Middleware;

/// <summary>
/// Gives every request a correlation identifier, echoes it, and pushes it into the log context.
/// </summary>
/// <remarks>
/// <para>
/// Accepts an inbound <c>X-Correlation-Id</c> so a caller — the single-page application, or
/// another service later — can tie its own logs to the server's. Generates one when absent, so
/// there is never a request without one.
/// </para>
/// <para>
/// It is echoed on the response because that is what makes it useful in a support conversation:
/// a customer reporting a failure can be asked for the identifier the error screen showed, and it
/// leads straight to the request's log lines. In production the global exception handler returns
/// this identifier and nothing else, so a stack trace never reaches a client while the operator
/// can still find the exact failure.
/// </para>
/// </remarks>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>The header carrying the identifier, inbound and outbound.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>The log-context property name, matched by the Serilog output template.</summary>
    public const string LogPropertyName = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = Resolve(context);

        context.TraceIdentifier = correlationId;

        // Set on OnStarting rather than directly: response headers cannot be written once the
        // response has begun, and a downstream component may start it before control returns
        // here — which would throw while handling a request that had otherwise succeeded.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context);
        }
    }

    private static string Resolve(HttpContext context)
    {
        string? inbound = context.Request.Headers[HeaderName].FirstOrDefault();

        // Bounded before it is echoed or logged. An unbounded caller-supplied value written into
        // a response header and a log file is a log-injection and header-smuggling surface.
        return string.IsNullOrWhiteSpace(inbound)
            ? Guid.CreateVersion7().ToString()
            : inbound[..Math.Min(inbound.Length, 64)];
    }
}
