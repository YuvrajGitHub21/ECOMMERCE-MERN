using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GroceryEasy.Infrastructure.Persistence;

/// <summary>
/// Readiness: the database is reachable <b>and</b> no migration is pending.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pending-migration check is the part that matters.</b> Migrations are applied as a
/// separate release step and never by the application at startup, which means it is entirely
/// possible to roll out an image whose code expects a column the database does not have. Without
/// this check that deployment goes live and fails one request at a time, on whichever endpoint
/// touches the new column first. With it, the container never passes its readiness probe, the
/// rollout halts, and the previous version keeps serving.
/// </para>
/// <para>
/// The legacy application's health endpoint returned the string "Hello World" without touching
/// the database at all, so it reported healthy while completely broken (L-12). A health check
/// that cannot fail is not a health check.
/// </para>
/// <para>
/// Liveness is deliberately a different, simpler thing — see the <c>/health/live</c> registration
/// in <c>Program.cs</c>. Liveness answers "should this process be restarted"; a database outage
/// is not a reason to restart a healthy process, so liveness must not depend on the database.
/// </para>
/// </remarks>
public sealed class DatabaseHealthCheck(ApplicationDbContext dbContext) : IHealthCheck
{
    /// <summary>Tag marking checks that belong to readiness rather than liveness.</summary>
    public const string ReadyTag = "ready";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The database is not reachable.");
            }

            IEnumerable<string> pending =
                await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);

            string[] pendingList = [.. pending];

            return pendingList.Length > 0
                ? HealthCheckResult.Unhealthy(
                    $"{pendingList.Length} migration(s) pending: {string.Join(", ", pendingList)}.")
                : HealthCheckResult.Healthy("Database reachable and schema up to date.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Any failure here is an unhealthy answer, not a 500 from the probe endpoint. An
            // orchestrator polling readiness needs a status, and an exception page is not one.
            return HealthCheckResult.Unhealthy("The database health check failed.", exception);
        }
    }
}
