# Backend language & platform choice: C#/.NET vs. the alternatives

## The criteria that actually matter for this workload

GroceryEasy is a transactional, relational, I/O-bound commerce backend — money, inventory, and orders, with a hard concurrency problem at its center (§D2 in [`engineering-decisions.md`](../engineering-decisions.md)) and a legacy audit full of bugs caused by *the language/runtime having no opinion about correctness*. The criteria that matter for **this specific shape of problem** — not backend development in general:

- **Runtime-enforced typing.** Not "has a type checker" — whether an invalid value can exist in memory at all, or only gets caught if someone remembered to run a separate validation step.
- **A concurrency model where CPU-bound work doesn't stall unrelated requests.** Most of this workload is I/O-bound (database round-trips, the Razorpay API), but pricing math over a large cart or image processing is not, and needs somewhere to run that doesn't block other users.
- **ORM/migration maturity for a relational, constraint-heavy schema.** `CHECK` constraints, foreign keys, versioned migrations, and conditional atomic `UPDATE` statements expressed cleanly.
- **A first-party (or de facto standard) identity/auth system.** Hand-rolled auth is exactly where the legacy app's worst bug (`L-08`) came from.
- **First-party real-time and background-job stacks**, since the product needs live order status (SignalR) and scheduled/retryable jobs (Hangfire) as core features, not afterthoughts.

## Comparison at a glance

| Criterion | C#/.NET | Node/TS | Python | Rust | Go | Java/Spring |
|---|---|---|---|---|---|---|
| Type safety | Enforced by the CLR at runtime | Erased at compile time | Optional, not runtime-enforced | Enforced by the compiler, strictest of all | Enforced by the compiler | Enforced by the JVM at runtime |
| Concurrency model | `Task`-based async, real thread pool | Single-threaded event loop | GIL blocks CPU-bound parallelism in-process | `async`/Tokio, no GC pauses | Goroutines, true multi-core by default | Threads / `CompletableFuture` |
| ORM maturity for complex relational queries | EF Core — LINQ, mature migrations | Prisma/TypeORM — weaker dynamic composition | Django ORM — mature, less async-native | Diesel (rigid, compile-time) / SeaORM (younger) | GORM (weaker complex queries) / sqlc (SQL-first) | Hibernate/JPA — mature, verbose (JPQL) |
| First-party Identity | ASP.NET Core Identity | None (Passport.js, third-party) | None (django-allauth, third-party) | None | None | Spring Security |
| First-party real-time / jobs | SignalR / Hangfire | Socket.IO / BullMQ (third-party) | Channels / Celery (third-party) | Younger crates | Third-party | STOMP over WebSocket / Quartz |
| Raw throughput ceiling (general benchmarks, e.g. TechEmpower) | Near the top | Mid-lower | Lower | Top | Near the top | Upper-mid |
| Dev velocity on fast-changing CRUD logic | High | High | High | Lower (ownership/borrow checker) | High | Medium (more boilerplate, improving with records/17+) |

## Why not each alternative — the technical case

### Node.js / TypeScript (NestJS + Zod + Prisma)

The closest "why didn't you just modernize what you had" question, and the most credible alternative.

- TypeScript's types are erased at compile time — `JSON.parse(req.body)` is `any` until a developer explicitly re-validates it (Zod/class-validator). Nothing in the language stops the gap; it's a discipline problem the legacy app failed at (`L-01`, `L-17`).
- Single-threaded event loop: a synchronous CPU-bound handler blocks *every other in-flight request*, not just its own, unless explicitly offloaded to `worker_threads` — an architectural decision every future contributor has to remember to make correctly.
- No first-party Identity/auth framework — the legacy password re-hash bug (`L-08`) is what "hand-rolled auth, no framework opinion" looks like in production.
- Prisma and TypeORM are both usable, but neither matches EF Core's LINQ-based, compile-time-checked dynamic query composition for the kind of filterable, faceted, paginated catalogue queries this project needs.
- **Where it would have won:** same language/runtime as the legacy app (faster to reach, ~30% less total effort), and the single-threaded model is genuinely a good fit for the I/O-bound majority of this workload.

### Python (Django / FastAPI)

- CPython's GIL prevents true CPU-bound parallelism inside a single process — async (FastAPI/`asyncio`) buys I/O concurrency, not multi-core CPU throughput, so scaling CPU-bound work needs more OS processes, with more memory overhead per unit of throughput than a real multi-threaded runtime.
- Type hints are optional and not enforced at runtime; `mypy` is a separate, skippable static-analysis step — the same erasure problem as TypeScript, in an ecosystem where duck typing is idiomatic and even more permissive by convention.
- Django's ORM is mature but weaker than EF Core for building up complex, conditionally-composed relational queries; its async story is newer and less battle-tested than ASP.NET Core's, which has been async-first for years.
- No first-party Identity-equivalent; `django-allauth` and similar are third-party.
- Python's actual strengths — fast prototyping, the data science/ML ecosystem — aren't the axis this project is optimizing for; it needs transactional correctness and typed contracts over money and stock.

### Rust (Axum/Actix + Diesel/SeaORM)

The strongest *technical* ceiling of any option on this list — worth saying plainly rather than dismissing.

- No garbage collector, memory safety guaranteed at compile time by the borrow checker, top-tier raw throughput and latency — the correct choice when the bottleneck is genuinely CPU or memory (a matching engine, a game server, a high-frequency proxy).
- This workload's bottleneck is never CPU or memory — it's I/O: database round-trips and the Razorpay API. Rust's biggest advantage doesn't move the needle for the actual problem being solved here.
- The ownership/borrow-checker model meaningfully slows iteration speed on fast-changing, CRUD-heavy business logic — exactly the kind of code a solo, ~14-week project adds and reshapes every week via vertical slices.
- Diesel is compile-time-checked but rigid and verbose for dynamic query composition; SeaORM is more flexible but meaningfully younger and less battle-tested than EF Core.
- No mature first-party equivalent of ASP.NET Identity, SignalR, or Hangfire — all would need to be hand-built or assembled from younger, less-proven crates.

### Go (net/http, chi, GORM/sqlc)

The closest "why not the simple, fast one" question.

- Concurrency model is genuinely comparable — goroutines multiplexed onto OS threads give true multi-core parallelism, arguably a simpler mental model than `Task`-based async/await. **Close to a wash against C# on this axis specifically**, not a point in C#'s favor.
- No generics-based LINQ-equivalent for composable relational queries: GORM (reflection-based ORM) is weaker on complex, dynamically-built queries; `sqlc` (compile-time-checked, generated from raw SQL) is excellent but SQL-first rather than composition-first — a different, not strictly worse, philosophy, but a poorer fit for this project's filter/facet/keyset-pagination query shapes.
- No EF Core-equivalent versioned-migrations-plus-change-tracking story.
- No built-in Identity/membership system — auth is hand-rolled or third-party, the same gap as Node.
- Go's error-as-values idiom is philosophically close to this project's `Result<T>` pattern (§C5) — the "explicit failure handling" argument that motivated `Result<T>` doesn't favor C# over Go; it's a wash there too.

### Java (Spring Boot + Hibernate)

The most honest near-tie of the entire list — say so directly if asked.

- Same category of statically-typed, GC'd, enterprise-grade runtime: a mature ORM (Hibernate/JPA), a real Identity-equivalent (Spring Security), mature scheduling (Quartz), solid WebSocket support.
- Would have been an **equally defensible technical choice.** The tilt toward C#:
  - LINQ gives compile-time-checked, composable queries directly in the language; JPQL/the Criteria API are more verbose and less type-safe for the same job.
  - Records, pattern matching, and nullable reference types give more expressive, less boilerplate-heavy domain modelling than pre-17 Java — though modern Java (17+, records, sealed classes) has closed most of this gap.
  - Minimal APIs + built-in OpenAPI generation is a lighter-weight default than Spring's controller-based conventions.
- None of these are decisive on their own. If pressed: *"Java/Spring would have been just as valid — I picked C#/.NET because the specific combination of EF Core, Identity, SignalR, and Hangfire is unusually cohesive for this exact shape of problem, and it matches my target market."*

## The one non-technical reason — stated separately, on purpose

A portfolio project's job is to be defensible in an interview at the kind of company you want to work for, and that target is a C# shop. This is a real, honest factor in the decision — but it is a **career/audience** reason, not an **engineering** one, and conflating the two is exactly what makes an answer sound weak under interview pressure. Keep them separate: lead with the technical criteria above, and if asked "would you have made the same call for a different target employer," the honest answer is that Java/Spring or a modernized Node stack would both have been legitimate technical alternatives — C# is the best fit *and* it's the right fit for the intended audience, not one masquerading as the other.

## In GroceryEasy

See `A1` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record of this decision, including the hybrid option (Node catalogue + .NET orders) and why it was rejected. The type-safety and identity-framework arguments above map directly onto audited legacy defects: `L-01` (client-supplied order totals), `L-08` (password re-hashed on every save), `L-17` (`pinCode`/`phoneNo` stored as JS `Number`) — see [`docs/legacy-audit.md`](../legacy-audit.md).

## Interview questions

**Q: Why C# and not Node, since you already knew the legacy codebase was Node?**
Reusing the language would have been ~30% faster, but the workload needs runtime-enforced typing and a real thread pool for CPU-bound work, and Node has neither natively — TypeScript's types vanish at runtime, and a single-threaded event loop means one slow synchronous handler blocks every other user. Both gaps map directly onto real bugs in the audited legacy app.

**Q: Isn't Rust strictly better technically — faster, memory-safe, no GC pauses?**
On raw performance and memory safety, yes, it has the highest ceiling of anything considered. But this application is I/O-bound — the database and the payment API are the bottleneck, not CPU or memory — so that ceiling doesn't translate into a real advantage here. The cost is real: the borrow checker slows iteration on fast-changing business logic, and the ORM/Identity/real-time ecosystem is younger than .NET's. Rust is the right call when the workload is actually CPU-bound at scale; this one isn't.

**Q: Python is popular for backends too (Django, FastAPI) — why not Python?**
Two structural gaps: the GIL prevents true CPU-bound parallelism within one process, and Python's type hints are optional and unenforced at runtime — the same erasure problem TypeScript has, in an ecosystem that's even more permissive about it by convention. Python's real strengths (fast prototyping, the ML ecosystem) aren't what this project needs; it needs typed contracts over money and stock.

**Q: Go looks simpler than C# and has a great concurrency story — why not Go?**
Honestly, the concurrency comparison is close to a wash — goroutines are a legitimate, arguably simpler alternative to `Task`-based async/await. The gap is ecosystem maturity for this specific domain: no LINQ-equivalent for composable relational queries, no EF Core-equivalent migrations story, and no first-party Identity system. It's a real contender for a smaller, less relationally-complex service.

**Q: Isn't Java basically the same as C#? Why not just use Spring Boot, since it's arguably more common in enterprise hiring?**
It's the closest peer on this whole list, and I'd call it a near-tie technically — Spring Security and Hibernate are genuinely comparable to ASP.NET Identity and EF Core. The tilt toward C# is LINQ's compile-time-checked query composition being cleaner than JPQL/Criteria API, plus a lighter-weight Minimal API + OpenAPI default. If the target employer were a Java shop, Spring Boot would have been an equally legitimate choice.

**Q: What's the concrete, in-the-code example of TypeScript's type erasure biting you?**
The legacy app stored `pinCode` and `phoneNo` as JavaScript `Number` — no compiler or runtime check stopped that, so leading zeros in a pincode were silently dropped. In C#, that column is `varchar`, enforced by both the domain model and a database constraint — there's no code path that can produce the bug, because the invalid state can't be represented.

**Q: Would you make the same choice if the target employer used a different stack?**
For a company running Node or Python already, modernizing in place would likely be the better call — you're optimizing for team fit and existing investment, not a from-scratch technical ceiling. For a company with genuinely CPU-bound, latency-critical infrastructure, Rust or Go would be worth a harder look. C#/.NET is the strongest fit for *this* workload (relational, transactional, I/O-bound, needs real-time and background jobs) and *this* target audience — those two happened to point the same direction here, which is worth saying plainly rather than implying one caused the other.
