using GroceryEasy.Application.Abstractions.Email;
using GroceryEasy.Infrastructure;
using GroceryEasy.Infrastructure.Identity;
using GroceryEasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace GroceryEasy.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against a real PostgreSQL in a container.
/// </summary>
/// <remarks>
/// <para>
/// <b>A real database, not the in-memory provider.</b> The in-memory provider is not a relational
/// database: it has no <c>citext</c>, no unique constraints that actually fire, no transactions
/// worth the name, and it silently accepts LINQ that PostgreSQL rejects. A test suite green
/// against it proves the code runs, not that it works. Every defect this project exists to design
/// out — the case-insensitive email collision, the atomic rotation, the constraint that makes
/// reuse detection possible — is invisible to it.
/// </para>
/// <para>
/// <b>One container for the whole run,</b> shared through a collection fixture. Roughly twenty
/// seconds to start, then fast. A container per test class turns a two-minute suite into twenty.
/// </para>
/// </remarks>
public sealed class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("groceryeasy_tests")
        .WithUsername("groceryeasy")
        .WithPassword("groceryeasy")
        .Build();

    private Respawner _respawner = null!;
    private NpgsqlConnection _connection = null!;

    /// <summary>Collects every email the application sends, so a test can read the token out of it.</summary>
    public CollectingEmailSender Emails { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Migrations are applied to a standalone context, BEFORE anything touches Services.
        //
        // This ordering is not incidental. WebApplicationFactory starts the host lazily on first
        // access to Services, and Program.cs seeds roles during startup — so migrating through a
        // scope taken from Services is already too late: the host has started, the seeder has run,
        // and it failed on a table that does not exist yet. The stack trace pointed at RoleSeeder
        // and said nothing about ordering.
        //
        // Doing it this way also matches production exactly, where migrations are a release step
        // that completes before the new image is allowed to start (ADR-0011). The test harness
        // should not need a sequence production does not have.
        DbContextOptions<ApplicationDbContext> options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_container.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;

        await using (ApplicationDbContext migrationContext = new(options))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // Now the host may start. Program.cs seeds the roles itself as it does.
        _ = Services;

        _connection = new NpgsqlConnection(_container.GetConnectionString());
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],

            // Not ignoring this wipes the migration history along with the data, so the next test
            // sees an empty database that the context believes is migrated. Every later test then
            // fails on a missing table, a long way from the cause.
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
    }

    /// <summary>Truncates every table between tests, leaving the schema and re-seeding roles.</summary>
    public async Task ResetDatabaseAsync()
    {
        await _respawner.ResetAsync(_connection);
        Emails.Clear();

        // Roles are reference data the reset removes, and registration assigns one, so they have
        // to come back before the next test runs.
        await RoleSeeder.SeedAsync(Services);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);

        // UseSetting, not ConfigureAppConfiguration.
        //
        // The factory's ConfigureAppConfiguration callback is appended to a configuration that
        // WebApplication.CreateBuilder has already built, and appsettings.Development.json — which
        // the Development environment loads — kept winning. The result was migrations applied to
        // the developer's local database on port 5432 while Respawn inspected the empty container
        // and reported "No tables found". Twenty-one tests failed with a message that pointed
        // nowhere near the cause.
        //
        // UseSetting writes into host configuration, which is established before the application's
        // own sources are read, so it takes precedence and the whole test host genuinely talks to
        // the container.
        builder.UseSetting(
            $"ConnectionStrings:{DependencyInjection.ConnectionStringName}",
            _container.GetConnectionString());

        // A signing key long enough for HMAC-SHA256 and obviously not a real one.
        builder.UseSetting("Jwt:SigningKey", "integration-tests-only-signing-key-0123456789abcdef");
        builder.UseSetting("Jwt:Issuer", "https://tests.groceryeasy.local");
        builder.UseSetting("Jwt:Audience", "groceryeasy-tests");
        builder.UseSetting("Jwt:AccessTokenMinutes", "15");
        builder.UseSetting("Jwt:RefreshTokenDays", "14");

        builder.UseSetting("Frontend:BaseUrl", "https://app.groceryeasy.local");

        builder.UseSetting("Email:Host", "localhost");
        builder.UseSetting("Email:Port", "1025");
        builder.UseSetting("Email:FromAddress", "no-reply@groceryeasy.local");
        builder.UseSetting("Email:FromName", "GroceryEasy Tests");

        builder.ConfigureTestServices(services =>
        {
            // The only substitution. Everything else — Identity, the token service, the database,
            // the whole pipeline — is the real thing, because those are what the tests are about.
            // Swapping the SMTP transport keeps the suite off the network and lets a test read the
            // verification token out of the message the application actually composed.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _container.DisposeAsync();
        await base.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
