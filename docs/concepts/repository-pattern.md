# Repository pattern

## What it is

Hides data access behind an interface that presents persistence as if it were an in-memory collection — `IOrderRepository.GetById(id)`, `Add(order)` — so the caller depends on an abstraction, not directly on the database technology. Originates from Domain-Driven Design (Eric Evans): a repository sits in front of an *aggregate root* and mediates all access to it.

Two very different implementations get called by the same name:

- **Generic repository** — `IRepository<T>` with `Get`/`Add`/`Update`/`Delete`/`Find`, generic over any entity type, usually paired with a `UnitOfWork` wrapper around `SaveChanges`. Common in tutorials and junior-authored codebases.
- **Narrow, aggregate-specific repository** — one repository per aggregate root, with intention-revealing methods that encode real query/loading logic (`IOrderRepository.GetForStatusTransitionAsync` — the right includes, the right tracking behavior for a state transition).

## Why the generic version is a code smell with EF Core specifically

`DbContext`/`DbSet<T>` already **is** a repository and a unit of work: it's a tracked, `IQueryable`-composable, swappable-for-tests abstraction over the database. Wrapping it again in a hand-rolled `IRepository<T>` is abstracting an abstraction, and it usually kills the ability to compose LINQ queries (`.Where().Include().Select()`) across layers — which is most of what makes EF Core worth using in the first place. It's a strong enough tell that "I built a generic repository over EF Core" reads, to a lot of .NET interviewers, as "I read one tutorial and didn't question it."

## Where else you'll see it

Generic repositories are everywhere in older enterprise .NET and in most "clean architecture starter template" GitHub repos — including a fair number of widely-starred ones, which is part of why the pattern persists. Narrow, purpose-built repositories are the more common recommendation in current EF Core guidance and in DDD-influenced codebases.

## In GroceryEasy

Narrow repositories only, added where genuine loading logic exists — not a blanket data-access layer. See `ADR-0006` and `C6` in [`docs/engineering-decisions.md`](../engineering-decisions.md):

- `IApplicationDbContext` — just the `DbSet<>`s plus `SaveChangesAsync`, exposed to `Application` handlers directly so they stay testable and `Application` never references the EF Core package itself.
- `IOrderRepository.GetForStatusTransitionAsync` — correct includes and tracking for a state transition.
- `IInventoryRepository.TryReserveAsync` — wraps the atomic conditional-`UPDATE` reservation.

Three repositories total, not fifteen. Cost: handlers touch `DbContext` directly for everything else, so a careless one can write an inefficient query — caught in review and by a dedicated N+1 sweep later in the project.

## Interview questions

**Q: Do you use the repository pattern?**
A narrow version, not the generic one — three repositories, each with intention-revealing methods, used only where real loading/query logic exists. Everywhere else, handlers use `DbContext` directly through an `IApplicationDbContext` abstraction, since `DbSet<T>` already behaves like a repository.

**Q: What's wrong with a generic `IRepository<T>`?**
It wraps an abstraction (`DbSet<T>`) that's already an abstraction, and it typically breaks `IQueryable` composition across layers — which is most of the value EF Core provides. It also tends to grow methods nobody uses ("just in case") because nothing forces it to stay minimal.

**Q: When *is* a repository interface worth adding?**
When there's real, non-trivial loading logic to encapsulate — specific includes, specific tracking behavior, or an operation (like an atomic conditional update) that needs to be centralized so every caller gets it right, rather than every caller re-deriving the correct query.
