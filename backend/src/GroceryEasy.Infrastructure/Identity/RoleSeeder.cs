using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GroceryEasy.Infrastructure.Identity;

/// <summary>
/// Ensures the four roles exist. Idempotent, and safe to run on every start.
/// </summary>
/// <remarks>
/// <para>
/// Roles are reference data, not schema, so they are seeded rather than written into a migration.
/// Putting them in a migration would mean editing applied history to add a fifth role later.
/// </para>
/// <para>
/// <b>This is not <c>Database.Migrate()</c> at startup and must not become it.</b> It only inserts
/// rows into a table the schema already guarantees. If the migration has not been applied, this
/// throws, the process stops, and <c>/health/ready</c> would have reported the same thing —
/// which is the intended behaviour, not something to work around by migrating here.
/// </para>
/// </remarks>
public static partial class RoleSeeder
{
    /// <summary>Creates any missing role.</summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        RoleManager<ApplicationRole> roleManager =
            scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        ILogger logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RoleSeeder));

        foreach (string roleName in Roles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            IdentityResult result = await roleManager.CreateAsync(new ApplicationRole(roleName));

            if (result.Succeeded)
            {
                LogSeeded(logger, roleName);
            }
            else
            {
                // Loud, and then fatal. A missing role means every authorization decision that
                // depends on it silently denies, which is a security failure that looks like a
                // user-experience bug and takes days to trace.
                throw new InvalidOperationException(
                    $"Could not seed role '{roleName}': "
                    + string.Join(' ', result.Errors.Select(error => error.Description)));
            }
        }
    }

    /// <summary>
    /// Source-generated so the argument is not boxed when the level is disabled.
    /// </summary>
    /// <remarks>
    /// Analyzer CA1873 flags the plain <c>LogInformation</c> overload for exactly that reason.
    /// It is a small cost here — this runs once per start — but the generated form is the house
    /// style for every log call in the codebase, and consistency is worth more than the exception.
    /// </remarks>
    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded role {RoleName}.")]
    private static partial void LogSeeded(ILogger logger, string roleName);
}
