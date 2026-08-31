# Clean Architecture

## What it is

A way of organizing a codebase into concentric layers where **source-code dependencies only point inward**, toward the business logic. Popularized by Robert C. Martin ("Uncle Bob") in 2012, formalizing ideas that already existed as [Hexagonal Architecture](https://en.wikipedia.org/wiki/Hexagonal_architecture_(software)) (Cockburn) and [Onion Architecture](onion-architecture.md) (Palermo).

The rings, from center outward:

1. **Entities / Domain** — business objects and rules that would exist even without a computer. No dependencies on anything.
2. **Use Cases / Application** — orchestrates entities to do something (`PlaceOrder`, `CancelBooking`). Depends only on Domain.
3. **Interface Adapters / Infrastructure** — implements the interfaces Application declares: database access, external APIs, file storage.
4. **Frameworks & Drivers / Presentation** — the web framework, the database driver, the UI. The outermost, most disposable ring.

The mechanism that makes this real rather than aspirational is the **Dependency Inversion Principle**: an inner layer *declares* an interface it needs (`IPaymentGateway`), and an outer layer *implements* it. A DI container wires the concrete implementation in at startup. The inner layer never references the outer layer's assembly at all — not even to know it exists.

## Where else you'll see it

Any codebase with a `Domain`/`Core` project that has zero third-party package references, and an `Infrastructure` project that implements interfaces declared in `Application`. It's the default recommended structure for non-trivial ASP.NET Core APIs, and the same idea appears under different names in Rails ("service objects" + POROs), Django, and Spring (`hexagonal`/`ports-and-adapters` style projects).

## In GroceryEasy

Used as the top-level project structure: `Domain ← Application ← Infrastructure ← Api`. Chosen over plain [N-tier](n-tier-architecture.md) (rejected), pure [vertical slices](vertical-slice-architecture.md) with no layer projects (rejected), and a modular monolith (overkill for one bounded context). See `C1` in [`docs/engineering-decisions.md`](../engineering-decisions.md).

Three concrete payoffs called out there:

- `Domain` has zero package references, so the pricing engine and order state machine are pure functions — unit-testable with no mocks, no database, no DI container.
- `Application` declares `IPaymentGateway`/`IObjectStorage`; `Infrastructure` implements them, so swapping Razorpay for a fake in tests is a DI registration, not a refactor.
- The boundary is machine-checked with NetArchTest (`Domain` references nothing, `Application` never sees EF Core), so it can't quietly erode over the life of the project.

Cost: more projects and ceremony — a trivial CRUD endpoint touches four of them — and the real risk of "Clean Architecture theatre," interfaces that exist only to satisfy the diagram, with only one real implementation ever.

## Interview questions

**Q: What's the Dependency Rule, in one sentence?**
Source-code dependencies can only point inward; nothing in an inner circle can know anything at all about an outer circle — not even that it exists.

**Q: How is this different from a normal layered / N-tier architecture?**
N-tier's dependency arrow points *down*, toward the database — `Presentation → Business → Data → DB`. Clean Architecture's arrow points *in*, toward the domain, using interfaces + dependency injection so `Infrastructure` depends on `Application`, not the other way around. See [n-tier-architecture.md](n-tier-architecture.md).

**Q: What's the actual cost of doing this?**
More indirection and more projects for simple operations, and the risk of building interfaces nobody will ever swap the implementation of — abstraction with no second implementation is just ceremony.

**Q: How do you stop it from rotting over time?**
Enforce it with an architecture test, not a code review checklist — e.g. NetArchTest asserting `Domain` has zero dependencies and `Application` never references the ORM.
