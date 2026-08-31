# Onion Architecture

## What it is

Jeffrey Palermo's 2008 pattern — the direct predecessor to [Clean Architecture](clean-architecture.md) — built on the same core idea: **the domain model sits at the center, and every dependency arrow points inward toward it.** The name comes from the diagram: concentric rings like an onion, with the domain as the core that never has to be peeled to be reached from outside.

Typical rings, center to edge:

1. **Domain Model** — entities, no dependencies.
2. **Domain Services** — operations that don't naturally belong to one entity.
3. **Application Services** — use-case orchestration, defines interfaces for anything external (`IEmailSender`, `IRepository`).
4. **Infrastructure / UI / Tests** — the outermost ring, implements those interfaces, contains the framework, the database, the web layer.

The rule Palermo emphasized: **infrastructure is never called directly — only through an interface the layer that needs it defines.** The database, the file system, and third-party services are all just outer-ring "details" that plug into the core, never the other way round.

## How it differs from Clean Architecture

In practice, barely — and most engineers use the two names interchangeably. The differences that do exist are mostly emphasis and vocabulary:

| | Onion (2008) | Clean Architecture (2012) |
|---|---|---|
| Origin | Jeffrey Palermo, blog post | Robert C. Martin, systematizing Onion + Hexagonal |
| Rings | Domain Model → Domain Services → Application Services → Infrastructure | Entities → Use Cases → Interface Adapters → Frameworks & Drivers |
| Central law | Infrastructure only reached through an interface | The explicit, named "Dependency Rule" |
| Where it's stricter | Less prescriptive about internal structure | Adds Interface Adapters as its own ring (presenters, controllers, gateways) |

[Hexagonal Architecture](https://en.wikipedia.org/wiki/Hexagonal_architecture_(software)) (Alistair Cockburn, 2005) is the third member of this family, predating both — it frames the same idea as symmetric "ports" the application exposes, with "adapters" plugging into them from either the driving side (a UI, a test) or the driven side (a database, an external API). All three — Hexagonal, Onion, Clean — are answers to the same question: *how do you stop business logic from depending on infrastructure?*

## In GroceryEasy

The project uses Clean Architecture's naming (`Domain`/`Application`/`Infrastructure`/`Api`) — see [clean-architecture.md](clean-architecture.md) and `C1` in [`docs/engineering-decisions.md`](../engineering-decisions.md). Worth knowing the Onion lineage exists and predates it, since interviewers sometimes use the two terms as if they were different choices rather than the same idea under two names.

## Interview questions

**Q: What's the difference between Onion and Clean Architecture?**
Onion came first and established "dependencies point inward, infrastructure is a detail reached only through an interface." Clean Architecture systematized the same idea with named rings and the explicit Dependency Rule. In modern .NET usage the terms are effectively interchangeable.

**Q: How does Hexagonal Architecture relate to these?**
Same family, older still (2005) — frames it as ports (interfaces the application defines) and adapters (implementations plugged in from outside), symmetric for both incoming (API, tests) and outgoing (database, external services) directions.

**Q: If they're basically the same, why do both names exist?**
Because they arose independently before converging on the same insight, and both stuck in the industry vocabulary. Knowing they're the same underlying idea — not competing alternatives — is itself the useful answer in an interview.
