# 0004 — Dependency governance: no MediatR, AutoMapper or FluentAssertions

- **Status:** Accepted
- **Date:** 2026-09-01

## Context

Three libraries appear in almost every ASP.NET Core codebase written in the last decade, and all three changed their licence during 2025:

| Library | What it does | Licence change |
|---|---|---|
| MediatR | In-process request dispatch, pipeline behaviours | Commercial from v13 |
| AutoMapper | Object-to-object mapping | Commercial, same maintainer |
| FluentAssertions | Test assertion syntax | Commercial from v8 |

They share a maintainer for the first two, and the pattern is the same in each case: a widely adopted free library moves to paid licensing at a major version. The versions published before the change remain free, so the tempting response is to pin the last free version and carry on.

This project needs a position on all three before the first handler is written, because they are exactly the kind of dependency that is cheap to adopt and expensive to remove once two hundred files import them.

## Decision

**Use none of the three. Pin no stale versions. Replace each with a first-party or actively maintained alternative.**

**MediatR → a hand-written dispatcher.** `ICommand<T>`, `IQuery<T>`, `ICommandHandler<,>`, `IQueryHandler<,>` and an `IDispatcher`, with handlers registered by assembly scan through **Scrutor** (Apache-2.0) and pipeline behaviours applied as a `Scrutor.Decorate<>` chain. Roughly 150 lines in `GroceryEasy.Application/Messaging/`. The behaviours — logging, validation, transaction — are the part that carries the value, and they were always ours to write.

**AutoMapper → manual mapping and query projection.** For reads, project inside the Entity Framework query with `.Select(...)` so PostgreSQL returns only the columns used. For writes, a plain mapping method next to the slice. This is also the structural fix for the legacy defect where list endpoints fetched whole documents — base64 image blobs included — to use two fields of them (L-18).

**FluentAssertions → Shouldly** (MIT, actively maintained). The assertion syntax differs slightly; the failure messages are comparably good.

Note the near-miss: **FluentValidation** is a different library from a different maintainer, remains Apache-2.0, and is used freely here. The names are close enough that `Directory.Packages.props` carries a comment saying so.

## Consequences

**Positive**

- No licence liability, and no clause that changes under the project later.
- The dispatcher is about 150 lines that can be stepped through in a debugger. A junior developer reading this codebase can see exactly how a command reaches its handler, which is not true of a reflection-heavy library.
- Implementing the mediator and decorator patterns demonstrates understanding of them; importing a package demonstrates having heard of them.
- Query projection is faster than mapping after materialisation, and the difference grows with row count.
- Dependencies stay few, which keeps the supply-chain surface small and `dotnet list package --vulnerable` short.

**Negative**

- The dispatcher is code this project now owns, tests and maintains. It is ~150 lines and covered by `DispatcherPipelineTests`, but it is not zero.
- MediatR's ecosystem — notification publishing, streaming, the `IPipelineBehavior` conventions people already know — is unavailable. Nothing here needs them yet; if notifications are wanted later, the Phase 4 outbox covers that ground better anyway.
- Manual mapping is more keystrokes than `_mapper.Map<T>()`, and a forgotten field is a bug the compiler will not catch on a record with optional members. Mitigated by projecting into positional records, where a missing argument *is* a compile error.
- Shouldly is less familiar than FluentAssertions to most .NET developers, so contributors pay a small syntax-learning cost.
- A future contributor may reach for one of these libraries by habit. The reason is written here, and `Directory.Packages.props` carries a pointer to it.

## Notes

The governance rule this generalises to: **prefer first-party or permissively licensed and actively maintained**, and treat a licence change as a reason to leave rather than to pin. A pinned stale version stops receiving security fixes while still looking maintained in the project file, which is the worst of both.
