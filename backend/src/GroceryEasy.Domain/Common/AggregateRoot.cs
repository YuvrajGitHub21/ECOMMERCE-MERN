namespace GroceryEasy.Domain.Common;

/// <summary>
/// An <see cref="Entity"/> that is the entry point to a consistency boundary, and the only
/// kind of entity permitted to raise domain events.
/// </summary>
/// <remarks>
/// Events are collected rather than dispatched. The aggregate records that something
/// happened; infrastructure decides when anyone hears about it — after
/// <c>SaveChangesAsync</c> succeeds, in the same transaction, via the outbox. Dispatching
/// from inside the aggregate would let a handler observe, or act on, a state change that
/// the transaction then rolls back.
/// </remarks>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Creates an aggregate with a freshly generated UUIDv7 identifier.</summary>
    protected AggregateRoot()
    {
    }

    /// <summary>Creates an aggregate with a caller-supplied identifier.</summary>
    protected AggregateRoot(Guid id) : base(id)
    {
    }

    /// <summary>Events raised since the aggregate was loaded or last cleared.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Discards the collected events. Called by infrastructure once they have been handed
    /// to the outbox, so a second <c>SaveChangesAsync</c> cannot publish them twice.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Records that something happened. Visible only to the aggregate itself.</summary>
    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
