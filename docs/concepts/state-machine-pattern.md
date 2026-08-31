# Order lifecycle: explicit finite state machine vs. an implicit status field

## What an explicit state machine is, generally

Any entity with a lifecycle — an order, a subscription, a support ticket — can be modeled two ways. **Implicitly**, as a bare field (`status: string` or a plain enum) that anything with write access can set to anything, with legality of a given transition, if enforced at all, scattered across whichever handlers happen to touch that field. **Explicitly**, as a genuine finite state machine: a fixed set of named states, a fixed set of legal transitions between them, one single path through which the field can ever change, and every other attempted change rejected by construction rather than by convention.

The difference only shows up under pressure — when a new screen, a new admin action, or a copy-pasted component gets write access to the field. An implicit model has no opinion about whether that new code path respects the rules that every *other* code path happened to respect; an explicit one enforces it the same way for every caller, including the one nobody has written yet.

## The criteria that actually matter for this decision

- **A transition either happens correctly or doesn't happen at all — never partially, never silently.** An order that's "sort of shipped" (stock decremented but status unchanged, or vice versa) is worse than one that visibly failed to transition.
- **Pickup and delivery are genuinely different lifecycles**, not the same states with different labels — a pickup order has a "ready for pickup, verify a code at the counter" phase that delivery never has, and delivery has an "out for delivery" phase that pickup never has. The model has to make an in-the-wrong-lifecycle transition simply not exist, not merely be discouraged.
- **A single, auditable mutation path.** When something goes wrong in production, "who changed this order's status, when, and why" has to be answerable from the data, not reconstructed from application logs.
- **The failure mode for an illegal transition has to be loud and typed**, not a thrown exception that looks like a bug, and not a silent no-op that looks like success. This is a direct extension of the `Result<T>` pattern — see [result-pattern.md](result-pattern.md).

## Comparison at a glance

| Criterion | Chosen: `FrozenSet` transition table + `TransitionTo` | Implicit status field + generic update | GoF State pattern (one class per state) |
|---|---|---|---|
| Illegal transition possible in code | No — rejected at the single mutation point | Yes — nothing stops any caller from setting any value | No — same guarantee, enforced through polymorphism instead of a lookup |
| Different lifecycles for pickup vs. delivery | Explicit — separate transition sets keyed by fulfilment type | Not modeled at all; one enum serves both, incorrectly | Explicit — separate state class hierarchies per fulfilment type |
| Failure signal on an illegal attempt | `Result.Failure`, typed and handled like any other expected outcome | Whatever the ORM does with an arbitrary value — usually nothing stops it | Typically an exception, or the transition method simply isn't exposed on that state's type |
| Where the rules live | One `FrozenSet<(Status,Status)>` per fulfilment type, small and declarative | Nowhere — "rules" are an assumption in each caller's head | Distributed across N state classes, one method implementation per legal transition |
| Cost to add or audit a transition | Add one tuple to a set; the `[Theory]` test grid updates itself | Unknowable without reading every call site that touches the field | Add a method to the relevant state class(es); more files, more ceremony |
| Right-sized for this problem | Yes — transitions have no state-specific *behavior*, only validity | No — this is the shape that produced the legacy bug | Overkill here — earns its cost only when each state has genuinely distinct behavior for the same message |

## Why the implicit version is the legacy bug, concretely

`[L-20]` is not a business-logic bug so much as a *missing* one. The customer-facing order detail screen was a placeholder (`<div>OrderDetails Nahi ho raha error solve</div>`), and the admin "process order" screen was a **verbatim copy-paste of the customer's shopping cart page** — it read `state.cart` instead of `state.orderDetails`, never called `useParams()`, and never dispatched the `updateOrder` action that already existed, fully wired, in the Redux store. The action, the reducer, and the API route were all real and all correct. Nothing was missing at the infrastructure level — what was missing was any code path that actually exercised them.

The consequence: **no order could ever legally be marked `Shipped`** in the running application, not because a rule forbade it, but because the screen that would have triggered it showed the admin their own cart instead. And because shipping was the *only* transition that decremented stock (`L-04`), **inventory was never decremented at all**, for the entire life of the app. A status field with no explicit transition model doesn't just fail to catch illegal transitions — it has no way to notice that an entire *legal* transition has silently become unreachable, because nothing in the system has an opinion about which transitions are supposed to exist in the first place. "The reducer exists" and "the transition happens" were indistinguishable, because nothing tested the second thing.

## The chosen design

`Order` exposes no public setter for status. The only way to change it is:

```csharp
public Result TransitionTo(OrderStatus target, Actor actor, string reason)
{
    if (!LegalTransitions[FulfilmentType].Contains((Status, target)))
        return Result.Failure(Error.InvalidTransition(Status, target));

    Status = target;
    _history.Add(new OrderStatusHistory(Status, target, actor, reason, _clock.UtcNow));
    return Result.Success();
}
```

Legal transitions are declared once, per fulfilment type, as data:

```csharp
private static readonly FrozenSet<(OrderStatus From, OrderStatus To)> PickupTransitions =
    new[]
    {
        (Placed, Confirmed), (Confirmed, ReadyForPickup),
        (ReadyForPickup, PickedUp), (Placed, Cancelled), (Confirmed, Cancelled),
    }.ToFrozenSet();

private static readonly FrozenSet<(OrderStatus From, OrderStatus To)> DeliveryTransitions =
    new[]
    {
        (Placed, Confirmed), (Confirmed, OutForDelivery),
        (OutForDelivery, Delivered), (Placed, Cancelled), (Confirmed, Cancelled),
    }.ToFrozenSet();
```

An illegal call — say, trying to move a **pickup** order to `OutForDelivery`, a state that only exists for delivery — returns `Result.Failure`, exactly like any other expected business-rule rejection (`OutOfStock`, `SlotFull`); it is not an exception, and it is not a silent no-op that leaves the caller believing the transition happened. See [result-pattern.md](result-pattern.md) for the general rule on when a failure is a `Result` versus a thrown exception — this is squarely the "caller can reasonably do something about it" case (show the operator "that's not a legal action from here," not a stack trace). Every successful transition writes an `order_status_history` row — actor, reason, timestamp — so "who moved this order, and why" is always answerable from the data, and a `[Theory]` test iterates every `(from, to, fulfilmentType)` tuple, legal and illegal, so "no path to `Shipped`" would fail a test the same afternoon it was introduced, instead of shipping invisibly for the life of the app.

### Why `FrozenSet`, specifically

`System.Collections.Frozen` (introduced in .NET 8) is built for exactly this shape of collection: constructed once, read very often, never mutated after construction. It trades a slower one-time build step for a faster `Contains` check than a regular `HashSet<T>` — the right trade for a transition table that's fixed at startup and consulted on every single status change, which is the hot path for every order in the system. A plain `HashSet<T>` or a `switch` expression would also be *correct*; `FrozenSet` is simply the tool that says, in the type itself, "this table does not change after startup," which a mutable collection can't.

### Why not the GoF State pattern

The classic "State" design pattern — one class per state, each implementing the operations valid from that state, with the context object holding a reference to its current state instance — is a legitimate, more object-oriented way to solve the same problem, and worth naming rather than dismissing. It earns its cost when different states need genuinely **different behavior** for the same message: a media player where `Play()` does materially different work depending on whether the player is `Playing`, `Paused`, or `Stopped`. Order transitions here have no such per-state behavior — every transition does exactly the same thing (check legality, mutate, append history); the only thing that varies is *which* transitions are legal from where. That's a validity table, not a set of state-specific behaviors, so a `FrozenSet` lookup is a simpler, equally correct model — and it's testable as one data-driven `[Theory]` over the whole matrix, rather than one test class per state class.

## In GroceryEasy

See `D5` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record. Closes [`L-20`](../legacy-audit.md#l-20) directly: an explicit transition matrix, unit-tested as a `[Theory]` over legal and illegal pairs, turns "no code path can ever reach `Shipped`" from an invisible production gap into a test that fails the day the gap is introduced.

## Interview questions

**Q: Why not just a status enum with a generic `PUT /orders/:id` update?**
Because nothing about a bare enum field stops any caller — a new admin screen, a script, a copy-pasted component — from setting it to any value from any other value. That's not hypothetical: the legacy admin "process order" screen was a copy-paste of the customer cart page that never called the (correct, fully wired) update action at all, so no order could ever legally reach `Shipped`, and since shipping was the only thing that decremented stock, inventory was never decremented for the life of the app. An explicit transition table makes an illegal or missing transition a compile-time-adjacent, test-covered fact instead of a silent gap.

**Q: Why return `Result.Failure` for an illegal transition instead of throwing?**
An illegal transition attempt — an operator clicking a button that shouldn't be legal from the order's current state — is an expected outcome the caller needs to render as "you can't do that from here," not a crash. That's exactly the line `Result<T>` draws generally: see [result-pattern.md](result-pattern.md). A silent no-op would be worse than either — the caller would believe the transition succeeded.

**Q: Why does pickup need a different transition table from delivery — isn't an order just an order?**
The two fulfilment types have genuinely different lifecycles: pickup has a "ready for pickup, verify a code at the counter" phase delivery never has, and delivery has an "out for delivery" phase pickup never has. If both shared one transition table, either the table would have to admit states that are meaningless for one fulfilment type, or the "invalid for this fulfilment type" check would have to live somewhere else, outside the transition model, exactly the kind of secondary rule that's easy to forget at a new call site.

**Q: Why `FrozenSet` instead of a regular `HashSet` or a `switch` statement?**
`FrozenSet` (from .NET 8's `System.Collections.Frozen`) is purpose-built for a collection that's built once and read constantly without ever mutating — it trades slower construction for faster lookups, which is the right trade for a transition table that's fixed at startup and checked on every order status change. A `HashSet` or `switch` would also work correctly; `FrozenSet` just encodes "this never changes after startup" in the type itself, which a mutable collection doesn't.

**Q: Isn't the classic GoF State pattern the "proper" way to do this?**
It's a legitimate alternative, and worth taking seriously rather than dismissing — it earns its cost when different states need genuinely different *behavior* for the same operation, like a media player where `Play()` does different work in `Playing` versus `Paused`. Order transitions here have no per-state behavior beyond "is this legal" — every transition does the same thing (check, mutate, log) — so a data-driven `FrozenSet` lookup is simpler and equally correct, and it's testable as one parameterized test over the whole matrix instead of one test class per state.
