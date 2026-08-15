namespace GroceryEasy.Domain.Common;

/// <summary>
/// Something that happened in the domain, raised by an <see cref="AggregateRoot"/>.
/// </summary>
/// <remarks>
/// Deliberately a bare marker. The obvious extra member would be an <c>OccurredOn</c>
/// timestamp, but Domain has no clock: <c>DateTime.UtcNow</c> is banned here and in
/// Application, because the slot-booking and token-expiry logic in later phases has to be
/// testable at an arbitrary instant. Occurrence time is stamped when the event is
/// published, by infrastructure that has the injected <see cref="TimeProvider"/>.
/// </remarks>
public interface IDomainEvent;
