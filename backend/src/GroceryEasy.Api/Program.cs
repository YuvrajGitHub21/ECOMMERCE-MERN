using System.Globalization;
using System.Security.Claims;
using System.Text;
using GroceryEasy.Api.Authentication;
using GroceryEasy.Api.Endpoints;
using GroceryEasy.Api.Middleware;
using GroceryEasy.Application;
using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Infrastructure;
using GroceryEasy.Infrastructure.Authentication;
using GroceryEasy.Infrastructure.Identity;
using GroceryEasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Serilog replaces the default logger outright rather than sitting beside it, so there is one
// pipeline and one output format. Compact JSON in Production because a log aggregator parses it;
// human-readable locally because a person reads it.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddEndpoints(typeof(Program).Assembly);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddAuthenticationLayer();
builder.Services.AddAuthorization();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(
        "database",
        tags: [DatabaseHealthCheck.ReadyTag]);

builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.UseExceptionHandler();

// Before the request log line, so every logged request carries the correlation id.
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();

// Liveness answers "should this process be restarted" and must not touch the database — a
// database outage is not a reason to kill a healthy process. Predicate excludes every check.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

// Readiness answers "should traffic be sent here" and does check the database, including whether
// a migration is pending. See DatabaseHealthCheck.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(DatabaseHealthCheck.ReadyTag),
});

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document is generated in every environment but only served in Development.
    // Publishing an API's full surface to anonymous callers in production is free reconnaissance.
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
}

await RoleSeeder.SeedAsync(app.Services);

await app.RunAsync();

/// <summary>
/// Authentication wiring. Lives here rather than in Infrastructure because it is HTTP middleware
/// configuration; Infrastructure owns token creation and validation logic, not the scheme.
/// </summary>
internal static class AuthenticationRegistration
{
    public static IServiceCollection AddAuthenticationLayer(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured through the options system rather than in the AddJwtBearer callback, so the
        // already-validated JwtOptions arrives by injection. The obvious alternative — calling
        // services.BuildServiceProvider() inside the callback — builds a SECOND container, which
        // gets its own singletons and is never disposed. It appears to work and quietly doubles
        // every singleton in the application.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                JwtOptions jwt = jwtOptions.Value;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,

                    // Zero, not the five-minute default. A fifteen-minute token with five minutes
                    // of slack is really a twenty-minute token, and the whole point of the short
                    // lifetime is bounding how long a leaked one stays useful.
                    ClockSkew = TimeSpan.Zero,

                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role,
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateSecurityStampAsync,
                };
            });

        return services;
    }

    /// <summary>
    /// Re-checks the token's security stamp against the user's current one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A valid signature proves this server issued the token. It does not prove the account still
    /// exists, still has the same roles, or still has the same password. Identity rotates the
    /// security stamp whenever any of those change, so comparing the claim against the stored
    /// value turns a stateless token into one that can be invalidated — at the cost of one
    /// indexed lookup per authenticated request, which is the right trade.
    /// </para>
    /// <para>
    /// This is the direct fix for L-07: in the legacy system a deleted user's token kept
    /// authenticating, and the request then died on a null dereference deeper in. Here the request
    /// fails authentication and returns <b>401, not 500</b>.
    /// </para>
    /// </remarks>
    private static async Task ValidateSecurityStampAsync(TokenValidatedContext context)
    {
        string? subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        string? presentedStamp = context.Principal?.FindFirstValue(AuthClaimTypes.SecurityStamp);

        if (!Guid.TryParse(subject, out Guid userId) || string.IsNullOrEmpty(presentedStamp))
        {
            context.Fail("The token is missing its subject or security stamp.");
            return;
        }

        UserManager<ApplicationUser> userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null || user.SecurityStamp != presentedStamp)
        {
            context.Fail("The account no longer exists, or its credentials have changed.");
        }
    }
}

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> in the integration tests has a
/// type to hang the test host off. Top-level statements generate this class as
/// <c>internal</c>, which the factory cannot reach; redeclaring it here as public is the
/// documented workaround and is cheaper than an <c>InternalsVisibleTo</c>.
/// </summary>
public partial class Program;
