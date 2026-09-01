using GroceryEasy.Application.Abstractions.Data;
using GroceryEasy.Application.Abstractions.Email;
using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Infrastructure.Authentication;
using GroceryEasy.Infrastructure.Configuration;
using GroceryEasy.Infrastructure.Email;
using GroceryEasy.Infrastructure.Identity;
using GroceryEasy.Infrastructure.Persistence;
using GroceryEasy.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
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

    /// <summary>Registers persistence, identity, tokens, mail and the clock.</summary>
    /// <exception cref="InvalidOperationException">
    /// The connection string is missing. Thrown at startup on purpose: a configuration mistake
    /// must stop the process at boot, not surface as a confusing failure on the first request
    /// that touches the database (L-12).
    /// </exception>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptionsWithValidation(configuration);
        services.AddPersistence(configuration);
        services.AddIdentityServices();

        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<IAuthNotificationService, AuthNotificationService>();

        return services;
    }

    /// <summary>
    /// Binds and validates every options class.
    /// </summary>
    /// <remarks>
    /// <c>ValidateOnStart</c> is the important half. Without it validation runs lazily, on first
    /// resolution, so a missing signing key surfaces as a failed login rather than as a process
    /// that refuses to start. Failing loudly at boot is what turns a configuration mistake into a
    /// deployment that never goes live instead of one that half-works (L-12).
    /// </remarks>
    private static void AddOptionsWithValidation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FrontendOptions>()
            .Bind(configuration.GetSection(FrontendOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
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
                .UseNpgsql(connectionString)
                // NOTE — no EnableRetryOnFailure here, and that is deliberate.
                //
                // The retrying execution strategy refuses to run inside a user-initiated
                // transaction: "NpgsqlRetryingExecutionStrategy does not support user-initiated
                // transactions." Every command in this system opens one, in
                // CommandTransactionBehavior, so enabling retries breaks every write endpoint —
                // found the moment the first registration was attempted.
                //
                // The documented fix is to wrap the whole unit in
                // Database.CreateExecutionStrategy().ExecuteAsync(...), which re-runs the entire
                // handler on a transient failure. That is wrong for handlers with side effects
                // outside the transaction: RegisterCommandHandler sends a verification email, and
                // a retry would send it twice.
                //
                // So: no retries until the outbox lands in Phase 4 and side effects move out of
                // the request, at which point the execution strategy can be applied deliberately
                // to handlers that are genuinely replayable. Revisit in Phase 6 alongside the
                // free-tier database, which is where transient failures actually start happening.
                // snake_case comes from EFCore.NamingConventions and NOT from Npgsql. Without
                // this line every table and column is PascalCase and needs quoting in SQL.
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        // Application depends on the interface; only this line knows which class satisfies it.
        services.AddScoped<IApplicationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());
    }

    /// <summary>
    /// Registers Identity's stores and the hand-built token layer.
    /// </summary>
    /// <remarks>
    /// <c>AddIdentityCore</c>, not <c>AddIdentity</c>. The latter also registers cookie
    /// authentication and its sign-in manager, which this API has no use for — it authenticates
    /// with bearer tokens — and registering both leaves two competing authentication schemes
    /// where one of them silently wins.
    /// </remarks>
    private static void AddIdentityServices(this IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Verification is required before an account can order. Enforced at login, so an
                // unverified user gets the same answer as a wrong password — see IdentityService.
                options.SignIn.RequireConfirmedEmail = true;

                // Length over composition. Long passphrases beat short passwords with a symbol
                // bolted on, and complexity rules mostly produce Password1! across every account.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // Lockout is what makes online password guessing impractical. The rate limiter in
                // Phase 6 covers the same ground at the edge; this covers it per account.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ITokenService, TokenService>();
    }
}
