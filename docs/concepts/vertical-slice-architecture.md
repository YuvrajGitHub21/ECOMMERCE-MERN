# Vertical Slice Architecture

## What it is

Organize code **by feature, not by technical role.** Instead of a folder of all controllers, a folder of all services, and a folder of all repositories ("horizontal" grouping by technical type), each use case gets one folder containing everything it needs:

```
Features/Orders/PlaceOrder/
    PlaceOrderCommand.cs
    PlaceOrderCommandHandler.cs
    PlaceOrderCommandValidator.cs
    PlaceOrderResponse.cs
    PlaceOrderEndpoint.cs
```

Popularized by Jimmy Bogard (also the author of [MediatR](mediatr.md)) as a reaction against the classic layered folder structure, where a single change ("add a field to place-order") means opening five different top-level folders, and where shared `IOrderService` classes accumulate every operation anyone ever needed on `Order` — a god-service nobody can safely change.

## Where else you'll see it

Common in modern .NET codebases that also use MediatR (the two pair naturally: one `IRequest`/`IRequestHandler` pair per slice, in one folder). The same instinct shows up as "feature folders" in other ecosystems — Angular and Next.js apps organized by route/feature instead of by `components/`, `services/`, `utils/`.

## It's an orthogonal decision to layering, not a replacement for it

Vertical slices answer *"how do I organize files within a layer?"* [Clean Architecture](clean-architecture.md) answers *"what's allowed to depend on what?"* — they're different axes, and can be combined: keep the four-project layer boundary (`Domain`/`Application`/`Infrastructure`/`Api`), but organize the *inside* of `Application` by feature rather than by technical type (`Commands/`, `Handlers/`, `Validators/`).

A codebase can also do *pure* vertical slices with no layer projects at all — genuinely faster for team velocity, but with nothing preventing a slice from reaching straight into `DbContext` or hiding an invariant in a handler. That's fine under high discipline; it's a gap if the whole point of the project is that invariants are structurally enforced rather than remembered.

## In GroceryEasy

Used *inside* Clean Architecture's `Application` layer, not instead of it — see `C2` in [`docs/engineering-decisions.md`](../engineering-decisions.md), planned as ADR-0003. Gets the compile-time boundaries of the layered structure and the locality of slices: everything a feature needs is one folder, so changing it means opening one place, not five.

Cost, stated plainly in the decision: some duplication between slices that a shared service would have centralized — accepted deliberately, because premature sharing between use cases is exactly how god-services are born.

## Interview questions

**Q: Doesn't Vertical Slice Architecture conflict with Clean Architecture?**
No — they answer different questions. Clean Architecture governs *what can depend on what* (layer boundaries); vertical slices govern *how files are organized within a layer* (by feature, not by technical type). GroceryEasy uses both: layer boundaries from Clean Architecture, feature folders inside `Application`.

**Q: What's the downside of organizing by feature?**
Some duplicated logic across slices where a shared helper or service could have centralized it. Accepted on purpose here — premature sharing between use cases tends to produce a shared service that quietly accumulates unrelated responsibilities over time.

**Q: What problem does it actually solve day to day?**
Locality. A developer changing `PlaceOrder` opens one folder and sees the command, handler, validator, and endpoint together, instead of hunting across `Controllers/`, `Services/`, `Validators/`, and `DTOs/` for the pieces of one feature.
