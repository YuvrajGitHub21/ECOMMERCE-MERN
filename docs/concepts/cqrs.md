# CQRS — Command Query Responsibility Segregation

## What it is

Split the model used for **writes** (Commands — change state, return little or nothing) from the model used for **reads** (Queries — return data, change nothing). Coined by Greg Young, extending Bertrand Meyer's older Command-Query Separation principle (a single *method* should either mutate state or return data, never both) up to the level of an entire application's request model.

It comes in two very different strengths:

- **Lightweight CQRS** — separate C# types for commands and queries, each with exactly one handler, still reading and writing the *same* database through the *same* ORM. This is the common, low-risk version.
- **Full CQRS** — separate read and write **databases** or schemas, with the write side (often event-sourced) projecting into a denormalized, query-optimized read side asynchronously. Buys read scalability and query models tailored exactly to each screen; costs eventual consistency (a write may not be visible on the read side immediately) and real operational complexity.

## Where else you'll see it

Lightweight CQRS is extremely common in modern ASP.NET Core codebases, almost always paired with [MediatR](mediatr.md) — `IRequest<TResponse>` for both commands and queries, one handler class each. Full CQRS with event sourcing shows up in domains with a genuine audit/replay requirement — banking ledgers, trading systems — where "what was the state at any point in time" matters as much as "what is the state now."

## CQRS and MediatR are independent decisions — the common conflation

Because MediatR is the default plumbing for lightweight CQRS in .NET, people often say "we use MediatR" when they mean "we do CQRS," and vice versa. They're separable:

- You can do CQRS with plain service classes and no mediator library at all — just a `CommandHandler` and `QueryHandler` naming convention and manual dispatch.
- You can use MediatR without CQRS — routing arbitrary requests to handlers with no command/query distinction at all.

## In GroceryEasy

Uses the lightweight form: every operation in `Application` is a `Command` or a `Query` type with exactly one handler, dispatched through a hand-rolled dispatcher rather than MediatR (see [mediatr.md](mediatr.md) and `ADR-0004`). No separate read database or event sourcing — reads project directly off the same PostgreSQL tables via EF Core `.Select()` projections (`C4` in [`docs/engineering-decisions.md`](../engineering-decisions.md)), which also means only the columns a screen actually needs are ever pulled from the database — the direct fix for the legacy app shipping entire product rows, base64 images included, on every list request (`L-18`).

## Interview questions

**Q: Do you need MediatR to do CQRS?**
No. CQRS is the separation of command and query *models*; MediatR is a library commonly used to route them to handlers. GroceryEasy does CQRS with a ~150-line hand-rolled dispatcher instead of MediatR.

**Q: What's the most extreme version of CQRS, and would you use it here?**
Fully separate read and write databases/models, usually paired with event sourcing on the write side. Not used here — the catalogue and order volume don't justify the eventual-consistency and operational cost; a single PostgreSQL database serves both sides fine.

**Q: What's the actual benefit of splitting commands from queries, even in the lightweight form?**
A command's contract can't accidentally return data a caller starts depending on, and a query's contract can't accidentally have a side effect — the type signature enforces Meyer's original Command-Query Separation principle at the request level, not just the method level.
