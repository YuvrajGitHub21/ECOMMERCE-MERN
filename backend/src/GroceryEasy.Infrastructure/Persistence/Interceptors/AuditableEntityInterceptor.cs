using GroceryEasy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GroceryEasy.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps <c>created_at</c> and <c>updated_at</c> on every <see cref="IAuditableEntity"/> as it
/// is saved.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an interceptor rather than a base-class <c>SaveChanges</c> override.</b> Both work.
/// The interceptor is registered as a service, so it takes <see cref="TimeProvider"/> by
/// injection and a test can hand it a fake clock without constructing a whole context. That
/// matters more than it looks: slot cut-offs, reservation expiry and token lifetimes all depend
/// on time being fakeable, and the habit is set here.
/// </para>
/// <para>
/// <b>Why never <c>DateTime.UtcNow</c>.</b> A direct call to the system clock inside domain or
/// application code makes any test that depends on time either slow, flaky, or both. There is
/// exactly one clock in this system and it arrives by injection.
/// </para>
/// <para>
/// Both timestamps are set on insert, so <c>created_at == updated_at</c> for a row that has
/// never changed. That reads better than a null and makes "rows never touched since creation" a
/// simple equality rather than an <c>IS NULL</c>.
/// </para>
/// </remarks>
internal sealed class AuditableEntityInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (EntityEntry<IAuditableEntity> entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                // HasChangedOwnedEntities is deliberately not consulted: this system has no
                // owned types yet. When it does, a modified owned entity must also bump the
                // parent's updated_at, and this is where that goes.
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;

                default:
                    break;
            }
        }
    }
}
