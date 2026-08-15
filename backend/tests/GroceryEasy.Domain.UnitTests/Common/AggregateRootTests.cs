using GroceryEasy.Domain.Common;

namespace GroceryEasy.Domain.UnitTests.Common;

public sealed class AggregateRootTests
{
    [Fact]
    public void NewAggregate_HasNoEvents()
    {
        var aggregate = new SampleAggregate();

        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Aggregate_IsAnEntity()
    {
        var aggregate = new SampleAggregate();

        aggregate.ShouldBeAssignableTo<Entity>();
        aggregate.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Raise_RecordsTheEvent()
    {
        var aggregate = new SampleAggregate();
        var raised = new SampleEvent();

        aggregate.DoSomething(raised);

        aggregate.DomainEvents.ShouldHaveSingleItem().ShouldBe(raised);
    }

    [Fact]
    public void Raise_KeepsEventsInOrder()
    {
        var aggregate = new SampleAggregate();
        var first = new SampleEvent();
        var second = new SampleEvent();

        aggregate.DoSomething(first);
        aggregate.DoSomething(second);

        aggregate.DomainEvents.ShouldBe([first, second]);
    }

    [Fact]
    public void Raise_RejectsNull()
    {
        var aggregate = new SampleAggregate();

        Should.Throw<ArgumentNullException>(() => aggregate.DoSomething(null!));
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheCollection()
    {
        // Infrastructure clears after handing events to the outbox. If this did not empty,
        // a second SaveChangesAsync in the same unit of work would publish them twice.
        var aggregate = new SampleAggregate();
        aggregate.DoSomething(new SampleEvent());

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void DomainEvents_CannotBeMutatedByCallers()
    {
        // ReadOnlyCollection<T> still implements ICollection<T>, so "is it assignable" proves
        // nothing. What matters is that the mutating members refuse: only the aggregate may
        // decide what it has raised.
        var aggregate = new SampleAggregate();
        var events = (ICollection<IDomainEvent>)aggregate.DomainEvents;

        Should.Throw<NotSupportedException>(() => events.Add(new SampleEvent()));
    }

    private sealed record SampleEvent : IDomainEvent;

    private sealed class SampleAggregate : AggregateRoot
    {
        public void DoSomething(IDomainEvent domainEvent) => Raise(domainEvent);
    }
}
