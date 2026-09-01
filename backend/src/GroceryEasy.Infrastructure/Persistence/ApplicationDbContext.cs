using System.Reflection;
using GroceryEasy.Application.Abstractions.Data;
using GroceryEasy.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GroceryEasy.Infrastructure.Persistence;

/// <summary>
/// The unit of work. Identity's tables plus the application's own, in one context and one
/// transaction scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>One context, not two.</b> Splitting Identity into its own <c>DbContext</c> is a common
/// pattern and it is wrong here: "create the user and write the audit row atomically" stops
/// being expressible the moment they sit in different contexts, because a transaction does not
/// span them without promoting to a distributed one.
/// </para>
/// <para>
/// <b>Extensions are declared on the model,</b> through <c>HasPostgresExtension</c>, rather than
/// as raw SQL inside a migration. Npgsql then emits them as an <c>AlterDatabase</c> operation
/// that migrations always order <em>before</em> any table that depends on them — which matters
/// because the users table uses <c>citext</c>. Declaring them here also means they exist in a
/// Testcontainers database, which never runs <c>deploy/postgres/init.sql</c>. Extensions that
/// live only in that script pass locally and fail in continuous integration.
/// </para>
/// </remarks>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IApplicationDbContext
{
    /// <summary>Issued refresh tokens, stored hashed. See <see cref="RefreshToken"/>.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <inheritdoc />
    public bool HasActiveTransaction => Database.CurrentTransaction is not null;

    /// <inheritdoc />
    public async Task<IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default) =>
        await Database.BeginTransactionAsync(cancellationToken);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Case-insensitive text, so Foo@x.com and foo@x.com cannot become two accounts.
        builder.HasPostgresExtension("citext");

        // Trigram similarity, for the typo-tolerant search fallback in Phase 2. Declared now
        // because adding an extension later is a migration nobody remembers to write.
        builder.HasPostgresExtension("pg_trgm");

        // Accent-insensitive matching for product names, also Phase 2.
        builder.HasPostgresExtension("unaccent");

        base.OnModelCreating(builder);

        RenameIdentityTables(builder);

        // One IEntityTypeConfiguration<T> per entity, discovered by scan. Fluent configuration
        // written inline here is how OnModelCreating becomes eight hundred unreadable lines.
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    /// <summary>
    /// Renames Identity's seven tables to snake_case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is needed at all.</b> <c>EFCore.NamingConventions</c> rewrites names that were
    /// chosen <em>by convention</em>. Identity does not choose by convention — it calls
    /// <c>ToTable("AspNetUsers")</c> explicitly — so the rewrite skips it and the schema comes
    /// out half snake_case (columns, and every table this project defines) and half PascalCase
    /// (Identity's tables). A schema that is inconsistent about quoting is a schema where every
    /// hand-written statement is a coin flip, and psql needs <c>"AspNetUsers"</c> in double
    /// quotes while <c>refresh_tokens</c> needs none.
    /// </para>
    /// <para>
    /// The <c>asp_net_</c> prefix is kept rather than shortened to <c>users</c> and <c>roles</c>.
    /// It is louder, and that is the point: these tables are owned by the framework, their shape
    /// is not ours to change, and nothing should be hand-writing inserts into them.
    /// </para>
    /// </remarks>
    private static void RenameIdentityTables(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("asp_net_users");
        builder.Entity<ApplicationRole>().ToTable("asp_net_roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims");
    }
}
