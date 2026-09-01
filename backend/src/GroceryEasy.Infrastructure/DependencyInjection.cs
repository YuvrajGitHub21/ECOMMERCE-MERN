using GroceryEasy.Application.Abstractions.Data;
using GroceryEasy.Infrastructure.Persistence;
using GroceryEasy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryEasy.Infrastructure;

/// <summary>
/// Wires up everything Infrastructure owns. The API calls this once and knows nothing about
/// Npgsql, Identity or the interceptors underneath.
/// </summary>
public static class DependencyInjection
{
    /// <summary>The connection-string name, matching <c>ConnectionStrings__Postgres</c> in
    /// <c>.env.example</c>. Named rather than typed inline so a rename is one edit.</summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>Registers persistence, the clock, and the save interceptors.</summary>
    /// <exception cref="InvalidOperationException">
    /// The connection string is missing. Thrown at startup on purpose: a configuration mistake
    /// must stop the process at boot, not surface as a confusing failure on the first request
    /// that touches the database (L-12).
    /// </exception>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString =
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings__{ConnectionStringName} — see .env.example.");

        // The one clock in the system. Tests swap in a FakeTimeProvider.
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    // Retry on the transient failures a free-tier or containerised database
                    // actually produces — a cold start, a failover, a dropped socket. Not a
                    // substitute for correctness under contention, which reservations handle.
                    npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
                })
                // snake_case comes from EFCore.NamingConventions and NOT from Npgsql. Without
                // this line every table and column is PascalCase and needs quoting in SQL.
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        // Application depends on the interface; only this line knows which class satisfies it.
        services.AddScoped<IApplicationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());

        return services;
    }
}
