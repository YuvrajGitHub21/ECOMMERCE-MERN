# 0006 — No generic repository: `DbContext` is the unit of work

- **Status:** Accepted
- **Date:** 2026-09-01

## Context

Clean Architecture requires that Application not depend on a specific database. The reflex answer in most .NET codebases is `IRepository<T>` with `GetAll`, `GetById`, `Add`, `Update`, `Delete`, plus an `IUnitOfWork` to commit — an abstraction over Entity Framework Core, which is itself already an abstraction over the database.

The question is what that second layer actually buys, given that `DbContext` is already a repository, a unit of work, a change tracker and an identity map.

## Decision

**No `IRepository<T>`. Application depends on `IApplicationDbContext`, which exposes the `DbSet<T>`s, `SaveChangesAsync` and a transaction handle. Narrow, aggregate-specific repositories are added later, and only where there is real loading or concurrency logic worth naming.**

Application references `Microsoft.EntityFrameworkCore` deliberately, for `DbSet<T>` and `IDbContextTransaction`. It does **not** reference the provider, and an architecture test asserts that Npgsql is absent from the assembly. That is the boundary that matters: Application knows there is a relational store, and does not know it is PostgreSQL.

Expected additions in later phases, each earning its existence:

- `IInventoryRepository.TryReserveAsync` — wraps the atomic conditional `UPDATE` and the rule that items are applied in `variant_id` order, which is what stops concurrent multi-item orders from deadlocking. That rule has to live in exactly one place or it decays.
- `IOrderRepository.GetForStatusTransitionAsync` — the specific `Include` graph a state transition needs.

Three such repositories are expected by the end of the project, not fifteen.

## Why not a generic repository

- **It throws away the useful parts of Entity Framework.** `GetAll()` returning `IEnumerable<T>` cannot express projection, `Include`, split queries, `AsNoTracking`, or a filter the database can index. Callers compensate by fetching whole entities and filtering in memory — which is precisely the legacy behaviour where list endpoints loaded entire documents, base64 images included, to read two fields of them (L-18).
- **Returning `IQueryable<T>` instead leaks worse.** The caller can now write any query, so the abstraction constrains nothing while still requiring everyone to learn it. It also means a provider-specific expression can be composed above the "abstraction" and fail at runtime.
- **The portability it promises is not real.** Swapping PostgreSQL for a document store would change the transaction model, the concurrency strategy and the query shapes. No interface survives that, and this project's design leans hard on things a document store cannot do — see [ADR-0002](0002-postgresql-over-mongodb.md).
- **Testability is not the reason either.** A repository mocked in a unit test asserts that the mock was configured correctly. Correctness here depends on constraints, transactions and concurrent behaviour, which is why the strategy is integration tests over a real PostgreSQL in Testcontainers.
- **It is a recognisable signal.** `IRepository<T>` layered over Entity Framework is the most common "read one blog post about Clean Architecture" marker in .NET codebases, and a reviewer reads it as pattern application without a reason.

## Consequences

**Positive**

- Handlers write the query they mean, including projection, so the database returns only the columns used.
- One less layer to write, test, document and step through.
- `DbContext` is the unit of work already, so "reserve stock, book the slot, create the order and write the outbox row, all or nothing" is expressible directly — and that transaction is the spine of Phase 4.
- The repositories that do get written are named after what they do, so their existence carries information.

**Negative**

- Application code contains LINQ that Entity Framework must be able to translate. A query that compiles and then throws at runtime is possible, and only an integration test catches it.
- There is no single place to intercept every read of an entity — useful for soft deletes or auditing. Global query filters cover the cases this project has, including the tenant filter in Phase 2.
- `IApplicationDbContext` grows a `DbSet` per aggregate, so the interface gets long. That is honest — it reflects the model's actual size rather than hiding it behind a generic.
- Replacing the ORM later would touch every handler. Accepted: that migration is not on any roadmap, and pretending otherwise costs work now for a benefit that never arrives.

## Related

- [`docs/concepts/repository-pattern.md`](../concepts/repository-pattern.md) — the pattern in general, and when a repository genuinely helps.
- `docs/legacy-audit.md` L-18 — the over-fetching defect this design makes hard to reproduce.
