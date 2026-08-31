using Microsoft.EntityFrameworkCore.Storage;

namespace GroceryEasy.Application.Abstractions.Data;

/// <summary>
/// The unit of work, as Application sees it.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no generic repository.</b> <c>DbContext</c> already is one — plus a unit of
/// work, plus a change tracker, plus an identity map. Wrapping it in
/// <c>IRepository&lt;T&gt;.GetAll()</c> throws away exactly the parts worth having:
/// projection, <c>Include</c>, split queries, and the ability to express a filter the database
/// can index. The legacy system's data layer was a set of hand-rolled wrappers and it is why
/// every list endpoint fetched whole documents to use two fields of them (L-18).
/// </para>
/// <para>
/// Application references <c>Microsoft.EntityFrameworkCore</c> for <c>DbSet&lt;T&gt;</c> and
/// this transaction handle, but never a provider. Nothing here knows the store is PostgreSQL;
/// an architecture test asserts that Npgsql is absent from this assembly, which is the boundary
/// that actually matters.
/// </para>
/// <para>
/// Aggregate-specific repositories are added later, and only where there is real loading logic
/// worth naming — never a <c>Repository&lt;T&gt;</c> over every table.
/// </para>
/// </remarks>
public interface IApplicationDbContext
{
    /// <summary>
    /// Whether a transaction is already open on this context.
    /// </summary>
    /// <remarks>
    /// Lets the transaction decorator join an outer transaction rather than opening a second
    /// one, which EF Core rejects at runtime. Relevant the moment one command dispatches
    /// another, and cheaper to have now than to debug later.
    /// </remarks>
    bool HasActiveTransaction { get; }

    /// <summary>Persists tracked changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a database transaction.</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
