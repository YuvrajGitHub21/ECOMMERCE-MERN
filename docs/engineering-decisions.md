# Engineering decisions and roadmap

Every significant technical choice in GroceryEasy, the alternatives weighed against it, and what each one costs — plus the phase-by-phase plan for building it.

## How to read this, and how it relates to the ADRs

This document is the **narrative overview**: readable top to bottom, one paragraph per decision, with the rejected options visible.

[`docs/adr/`](adr/README.md) holds the **formal record**: one file per decision, MADR format, immutable once accepted, with full context and consequences. Where an ADR exists it is authoritative and linked below.

The rule that keeps these from drifting: **this document summarises, ADRs decide.** If they ever disagree, the ADR wins and this file is wrong.

A third document, [`docs/legacy-audit.md`](legacy-audit.md), catalogues the 20 defects in the pre-rewrite MERN app. Many decisions here exist specifically to make one of those defect classes unrepresentable; those are tagged `[L-nn]`.

A fourth, [`docs/plans/`](plans/README.md), holds the original working plans written before each phase began — useful for the reasoning behind the phase sequencing, but not kept in sync with implementation. Where it disagrees with this document, this document wins.

---

# Part 1 — Decisions

## A. Platform

### A1. ASP.NET Core (C#) over Node.js

**Options:** keep Node/Express and modernise it (TypeScript, NestJS, Zod, Prisma) · rewrite in ASP.NET Core · hybrid, keeping Node for the catalogue and extracting orders into a .NET service.

**Chosen: full ASP.NET Core rewrite.**

**The technical criteria that actually drove it** — properties this specific workload needs, not a subjective preference:

- **Runtime-enforced typing, not just editor-time typing.** TypeScript's types are erased at compile time — nothing stops a malformed JSON body from being assigned to a typed variable at runtime; validation is a separate, easy-to-skip step (Zod, class-validator). C#'s type system is enforced by the CLR itself: a `decimal` field cannot hold a string, ever. This maps directly onto two audited legacy bugs: `L-17` (`pinCode`/`phoneNo` stored as JS `Number`, silently losing leading zeros) and `L-01` (arbitrary JSON accepted straight into order totals, with no structural barrier stopping it).
- **A concurrency model where CPU-bound work doesn't stall the whole process.** Node runs a single-threaded event loop — a synchronous CPU-bound handler (bulk pricing math over a large cart, image resizing) blocks *every other in-flight request* until it returns, unless a developer remembers to offload it to `worker_threads`. ASP.NET Core's `Task`-based async model runs on a real, auto-scaling thread pool: a slow handler ties up one worker thread, not the server's ability to serve everyone else.
- **A first-party, vetted identity system.** ASP.NET Core Identity ships in the framework — password hashing, lockout, security stamps, token providers, all vetted. Node has no canonical equivalent; Passport.js and NestJS's auth modules are thin wrappers a team still has to get right itself. The legacy app's actual auth bug (`L-08`, a missing `return` that re-hashed the password on every save) is exactly the failure mode of hand-rolling this without a framework-owned answer.
- **A mature ORM for a genuinely relational, constraint-heavy schema.** EF Core's LINQ-to-SQL translation, first-class versioned migrations, and change-tracking are ahead of Prisma (weaker for building up complex, conditionally-composed queries across methods than LINQ's expression trees) and TypeORM (widely reported migration-generation reliability issues) for a schema that leans on `CHECK` constraints, foreign keys, and a hand-written conditional `UPDATE` for inventory concurrency (`D2`).
- **First-party real-time and background-job stacks.** SignalR and Hangfire are mature and either first-party (SignalR) or the de facto standard (Hangfire); Node's equivalents (Socket.IO, BullMQ) are perfectly good but third-party, with more integration surface to own.

None of this makes Node *wrong* — a TypeScript/NestJS rebuild would have been perfectly respectable and roughly 30% faster to reach, and Node's single-threaded model is genuinely excellent for I/O-bound throughput, which is most of this workload. **The one honest non-technical factor**, stated separately so it doesn't get confused with the engineering case above: a portfolio project's job is to be defensible in an interview at the kind of company you want to work for, and that target is a C# shop. That reason explains the *timing and audience* of the decision — not its technical merit — and shouldn't be the answer given when an interviewer asks "why C#" technically.

The hybrid (Node for catalogue, .NET for orders) was rejected as the worst of both: two stacks to maintain, two deployment stories, and a thinner C# surface than either pure option.

**Cost:** everything is rewritten rather than improved, so nothing ships until Phase 4. All Node/Express knowledge embedded in the old codebase is discarded rather than built on.

**Why not Python, Rust, Go, or Java?** These weren't live options during planning — the legacy app was already Node, so the real decision was *modernise in place vs. rewrite in C#* — but each is a credible backend platform an interviewer may raise. The one-line technical verdict on each, with the full case, a comparison table, and rehearsable Q&A in [`docs/concepts/language-platform-choice.md`](concepts/language-platform-choice.md):

- **Python (Django/FastAPI):** the GIL blocks true CPU-bound parallelism within one process; type hints are optional and not runtime-enforced — the same erasure problem as TypeScript, in an ecosystem more permissive about it by convention.
- **Rust (Axum/Actix + Diesel/SeaORM):** the strongest technical ceiling on raw performance and memory safety of any option considered, but this workload is I/O-bound, not CPU-bound, so that ceiling doesn't move the needle here — while the borrow checker meaningfully slows iteration speed on fast-changing CRUD business logic, and the ORM/Identity/real-time ecosystem is younger than .NET's.
- **Go:** a genuinely comparable concurrency model (goroutines vs. `Task`-based async — close to a wash), but no LINQ-equivalent for composable relational queries, no EF Core-equivalent migrations story, and no first-party Identity system.
- **Java (Spring Boot):** the most honest near-tie of the four — a mature ORM (Hibernate/JPA), a real Identity-equivalent (Spring Security), mature scheduling (Quartz). It would have been an equally defensible technical choice; C# wins mainly on LINQ's compile-time-checked query composition versus JPQL/Criteria API, and otherwise the decision comes down to the same audience-fit reason as above.

### A2. .NET 10 (LTS) — [ADR-0013](adr/0013-target-dotnet-10-lts.md)

**Options:** .NET 9 (STS) · .NET 10 (LTS).

.NET 9 left support on 2026-05-12, before this project started. Beginning a rewrite on an unsupported framework means shipping without security patches on day one. LTS runs to November 2028, so the repository still builds and deploys when someone opens it eighteen months from now.

Also brings things the design already assumes: `Guid.CreateVersion7()`, `HybridCache`, built-in OpenAPI, `TimeProvider`.

**Cost:** a few third-party packages may lag on .NET 10 support.

**Deeper dive:** [`docs/concepts/dotnet-version-choice.md`](concepts/dotnet-version-choice.md).

### A3. PostgreSQL over MongoDB — [ADR-0002](adr/0002-postgresql-over-mongodb.md)

**Options:** stay on MongoDB with the C# driver · PostgreSQL + EF Core · Postgres for transactional data with Mongo retained for the catalogue.

The domain is relational and always was: orders reference variants, variants reference products, products belong to stores. Modelling that as documents means duplicating data or hand-rolling joins — which is what the legacy app did, and why order line items silently drifted from the products they referenced.

What Postgres buys, concretely: **real transactions** (reserve inventory + book slot + create order + write outbox, all or nothing), **constraints the application cannot bypass** (`CHECK (rating BETWEEN 1 AND 5)` `[L-19]`, `unique (product_id, user_id)` `[L-05]`), **atomic conditional updates** for inventory `[L-04]`, versioned migrations, built-in full-text search, and `numeric` money with no floating-point error.

The split option was rejected as unjustifiable complexity: two databases and a sync problem, for a catalogue of a few thousand rows.

**Cost:** schema changes need migrations. A one-time migration tool must be built. Embedded documents (reviews, addresses) become joins.

**Deeper dive:** [`docs/concepts/database-choice.md`](concepts/database-choice.md).

---

## B. Migration strategy

### B1. Clean-room rewrite over strangler-fig — [ADR-0001](adr/0001-clean-room-rewrite-over-strangler-fig.md)

**Options:** strangler-fig behind a YARP gateway · clean-room rewrite with one-time data migration · incremental in-place refactor.

The strangler pattern solves exactly one problem: migrating a system carrying **live traffic that cannot be dropped**. This system has no users and ~20 seed products. Applying the pattern anyway costs a YARP gateway, a cross-stack auth bridge between `jsonwebtoken` and `JwtBearer`, and Mongo↔Postgres dual-write — roughly **3–4 weeks, a quarter of the budget**, on scaffolding built to be deleted.

"The app is never broken" is preserved by a different seam: the legacy app is frozen and stays runnable; the new SPA replaces the public URL once, at the end of Phase 4.

**Cost:** no incremental delivery — nothing user-visible ships until Phase 4. The cutover is one unrehearsed event. Forgoes hands-on experience with a genuinely valuable industry pattern, partly offset by prototyping YARP as a one-evening artifact in Phase 6 and documenting it as *evaluated and rejected*.

**Deeper dive:** [`docs/concepts/migration-strategy.md`](concepts/migration-strategy.md).

### B2. Freeze and document the legacy defects rather than fix them

**Options:** fix all 20 defects in Express first · fix only the ~6 critical ones · freeze and document.

Patching code that gets deleted in six weeks costs 2–3 weeks of a 14-week budget. The alternative deliverable — [`docs/legacy-audit.md`](legacy-audit.md), where each defect is mapped to the design element that makes its *class* unrepresentable — is both cheaper and a far better artifact. *"I audited my own three-year-old code, found a forgeable order total and an admin guard that had never executed, and here is the architecture that makes each one impossible"* is a senior narrative. *"I added a null check"* is not.

**Cost:** the frozen demo is exploitable if anyone pokes it. Mitigated by it not being publicly linked.

**Deeper dive:** [`docs/concepts/technical-debt-triage.md`](concepts/technical-debt-triage.md).

---

## C. Application architecture

### C1. Clean Architecture over N-tier, pure vertical slices, or a modular monolith

**Options considered:**

| Option | Why not |
|---|---|
| **Traditional N-tier** (Controllers → Services → Repositories → DB) | The dependency arrow points *down* to the database, so domain logic degenerates into transaction scripts over EF entities. The legacy app is the worked example: pricing and stock rules lived in the HTTP controller, so hitting a different route bypassed them entirely `[L-01]`, `[L-03]`. Nothing at compile time prevents it. |
| **Pure vertical slices** (feature folders, no layer projects) | Genuinely good, and faster for team velocity. But with no layer boundary, nothing *prevents* a slice from reaching into `DbContext` or putting an invariant in a handler. That is fine when discipline is high — but this project's whole thesis is that invariants are structurally enforced rather than remembered. |
| **Modular monolith** (module per bounded context) | The right answer at larger scale or with several bounded contexts. Here there is essentially one — a store's commerce flow. Module boundaries would be speculative, buying isolation nobody needs. |

**Chosen: Clean Architecture** — `Domain` ← `Application` ← `Infrastructure` ← `Api`, with dependencies pointing inward.

Three concrete reasons, not aesthetics:

1. **`Domain` has zero package references**, so the pricing engine and order state machine are pure functions — unit-testable with no mocks, no database, no DI container. That is where correctness lives, and it is the part worth testing densely.
2. **Dependency inversion makes the seams real.** `Application` declares `IPaymentGateway` and `IObjectStorage`; `Infrastructure` implements them. Swapping Razorpay for a fake in tests is a DI registration, not a refactor.
3. **The boundaries are machine-checkable.** NetArchTest asserts that `Domain` references nothing and `Application` never sees EF Core, so the architecture cannot quietly erode over 14 weeks. An architecture nobody can violate is worth more than one everybody agrees with.

**Cost:** more projects and more ceremony — a trivial CRUD endpoint touches four of them. The real risk is **Clean Architecture theatre**: interfaces that exist only to satisfy a diagram. Mitigated deliberately by C6 (no generic repository — three repositories total) and by projecting directly in EF queries rather than mapping through layers.

**Deeper dive:** [`docs/concepts/clean-architecture.md`](concepts/clean-architecture.md) · [`onion-architecture.md`](concepts/onion-architecture.md) · [`n-tier-architecture.md`](concepts/n-tier-architecture.md).

### C2. Vertical slices *inside* the layers — ADR-0003 (Phase 1)

Not either/or with C1. Keep the four-project boundary, but organise `Application` **by feature, not by technical type**:

```
Features/Orders/PlaceOrder/
    PlaceOrderCommand.cs
    PlaceOrderCommandHandler.cs
    PlaceOrderCommandValidator.cs
    PlaceOrderResponse.cs
```

A folder of forty `IOrderService.cs` interfaces is the junior smell; co-located slices are the senior one. Everything a feature needs is in one folder, so changing it means opening one place. You get the compile-time barriers of layers *and* the locality of slices.

**Cost:** some duplication between slices that a shared service would have centralised — accepted, because premature sharing between use cases is how god-services are born.

**Deeper dive:** [`docs/concepts/vertical-slice-architecture.md`](concepts/vertical-slice-architecture.md).

### C3. Minimal APIs over MVC controllers — ADR-0014 (Phase 1)

**Options:** MVC controllers (`ControllerBase` + `[ApiController]`) · Minimal APIs with a `MapGroup` + `IEndpoint` convention · FastEndpoints.

**MVC controllers** are the default answer and have real advantages: every .NET developer knows them, `[ApiController]` gives automatic model-state validation and `400` responses, action filters are a mature extension point, and attribute routing is well understood.

The problem is that **a controller groups by resource, while a vertical slice groups by use case**, and those are different axes. An `OrdersController` accumulates twelve actions and six injected services — `PlaceOrder` needs the pricing engine, `GetMyOrders` needs none of it, and both drag the other's dependencies through the constructor. The class becomes a unit of grouping with no relationship to how the code actually changes. Having chosen vertical slices in C2, controllers would immediately fight that decision.

**Chosen: Minimal APIs behind an `IEndpoint` convention.**

```csharp
public interface IEndpoint { void MapEndpoint(IEndpointRouteBuilder app); }
```

One endpoint per type, auto-registered by assembly scan, living in the slice folder beside its command, handler and validator. Concretely this buys:

- **One feature, one folder.** The HTTP shape sits next to the logic it exposes.
- **Dependencies are per-endpoint**, injected into the handler delegate rather than a shared constructor.
- **`TypedResults` gives compile-checked response types** that flow automatically into the OpenAPI document — which is what makes the generated TypeScript client trustworthy, and permanently kills the client/server contract mismatch that made every legacy error toast display `undefined` `[L-12]`.
- Lower per-request overhead — no controller activation or filter pipeline.

**FastEndpoints** was rejected on dependency grounds: it solves with a third-party framework at the HTTP boundary what ~30 lines of convention code solves, and after the licensing lesson in C4, adding a framework there is precisely the risk we just decided to stop taking.

**Cost:** you must write the registration convention yourself, or `Program.cs` becomes a wall of `app.MapPost(...)` calls. Endpoint filters are less capable than MVC action filters — cross-cutting concerns move into the Application pipeline behaviours instead, which is arguably where they belonged. And a controller-native developer joining later needs a short ramp.

**Deeper dive:** [`docs/concepts/minimal-apis.md`](concepts/minimal-apis.md) · [`mvc-controllers.md`](concepts/mvc-controllers.md).

### C4. No MediatR, AutoMapper or FluentAssertions — ADR-0004 (Phase 1)

**MediatR** moved to a commercial licence at v13 (September 2025); **AutoMapper** followed from the same vendor; **FluentAssertions** did the same at v8. Taking a paid dependency on a portfolio project is wrong, and silently pinning the last free version is worse — it looks like an oversight rather than a decision.

Replacements: a **hand-rolled dispatcher** (~150 lines including behaviours, registered via Scrutor, with the pipeline as a `Scrutor.Decorate<>` chain), **manual mapping** via `ToResponse()` extension methods next to each slice, and **Shouldly** for assertions.

Manual mapping is not a consolation prize. AutoMapper moves mapping bugs from compile time to runtime and makes "find usages" useless. For queries, projecting directly in the EF query (`.Select(p => new ProductListItem { ... })`) means Postgres returns only the columns actually used — which structurally prevents the legacy failure of shipping the entire catalogue including base64 image blobs on every request `[L-18]`.

**Cost:** ~150 lines to write and own. Debugging a `Scrutor.Decorate` chain is less documented than MediatR's. And "used MediatR" is a phrase some job descriptions literally list — mitigated by the ADR, since *understanding the pattern well enough to implement it* is the stronger claim.

**Deeper dive:** [`docs/concepts/mediatr.md`](concepts/mediatr.md) · [`cqrs.md`](concepts/cqrs.md) · [`object-mapping-strategy.md`](concepts/object-mapping-strategy.md) · [`xunit-vs-nunit.md`](concepts/xunit-vs-nunit.md) (for the FluentAssertions/Shouldly half of this decision).

### C5. `Result<T>` for expected failures, exceptions for bugs — ADR-0005 (Phase 1)

**Options:** exceptions for everything · `Result<T>` for everything · a split.

```
Result   ← OutOfStock, SlotFull, PincodeNotServiceable, CartEmpty, StoreClosed
throw    ← null argument, invariant violation, DB unreachable, misconfiguration
```

The distinction is whether the caller can reasonably do something about it. "Out of stock" is a normal business outcome the UI must render nicely; it is not exceptional and should not cost a stack unwind. A null argument is a bug and should be loud.

A single `Result → IResult` extension maps `ErrorType` to **RFC 9457 ProblemDetails**, giving one response shape, always.

**Cost:** more verbose than throwing — every call site handles both branches. Requires discipline, since C# has no exhaustiveness checking to enforce it.

**Deeper dive:** [`docs/concepts/result-pattern.md`](concepts/result-pattern.md).

### C6. No generic repository — ADR-0006 (Phase 1)

`DbContext` **is** the unit of work; `IApplicationDbContext` (just the `DbSet<>`s plus `SaveChangesAsync`) is exposed from Application so handlers stay testable and Application never references the EF provider.

Narrow, aggregate-specific repositories are added **only where real loading logic exists** — `IOrderRepository.GetForStatusTransitionAsync` (correct includes and tracking), `IInventoryRepository.TryReserveAsync` (wraps the atomic conditional update). **Three repositories, not fifteen.**

A generic `IRepository<T>` over EF is the loudest "I read one blog post" signal in .NET: it wraps an abstraction that is already an abstraction, and it destroys `IQueryable` composition, which is the main thing EF is for.

**Cost:** handlers touch `DbContext` directly, so a careless one can write an inefficient query. Caught in review and by the N+1 sweep in Phase 6.

**Deeper dive:** [`docs/concepts/repository-pattern.md`](concepts/repository-pattern.md).

---

## D. Correctness

These are the decisions the project is actually about. Each closes a defect class from the audit.

### D1. Server-authoritative pricing `[L-01]`

`PlaceOrderCommand` carries **no money fields at all** — only `cartId`, `fulfillmentType`, `slotId`, `addressId`. Prices are read from the database inside the handler and run through `OrderPricingEngine`, a pure function.

The alternative — validating client-supplied totals against recomputed ones — was rejected because it leaves a code path that *accepts* a price. If the field does not exist in the contract, no bug of that shape can exist. GST is extracted from inclusive prices (Indian retail convention) and split CGST/SGST; rounding is `MidpointRounding.AwayFromZero` at 2dp with the grand total reconciled to the sum of lines.

**Deeper dive:** [`docs/concepts/server-authoritative-pricing.md`](concepts/server-authoritative-pricing.md).

### D2. Inventory concurrency — conditional `UPDATE` — ADR-0007 (Phase 4)

**Options:**

| Option | Why not |
|---|---|
| `SELECT ... FOR UPDATE` | A multi-item order locks N rows; two concurrent orders touching the same items in different order **deadlock**. Sorting by id fixes it, but then correctness depends on every future caller remembering to sort. |
| Optimistic concurrency (`xmin`) on the inventory row | Correct, but under contention on a hot SKU it retries, and each retry re-runs the whole handler. Kept for *admin* edits, where contention is low and "someone else changed this" is the right UX. |

**Chosen:** a reservation table with a TTL, plus one atomic statement:

```sql
UPDATE inventory SET reserved = reserved + @qty
 WHERE variant_id = @id AND on_hand - reserved >= @qty
```

Postgres serialises writers on the row internally. **"Zero rows affected" *is* the out-of-stock answer**, with no read-then-write window — the exact gap that made the legacy decrement lose updates and go negative `[L-04]`. Items are always applied in `variant_id` order, enforced in one place with a test, so multi-item orders cannot deadlock. Every movement writes an append-only `stock_ledger` row, so drift is detectable rather than invisible.

Proven by an integration test firing **20 concurrent orders at 10 units of stock**: exactly 10 succeed, 10 return `OutOfStock`, stock lands on 0, ledger sums to zero.

**Deeper dive:** [`docs/concepts/inventory-concurrency-strategies.md`](concepts/inventory-concurrency-strategies.md).

### D3. Idempotency keys `[L-15]`

The client generates an `Idempotency-Key` **once when the checkout screen mounts** — not per click — so a double-click, a retry, and a flaky-network resend all collapse to one order. The server `INSERT`s the key first: a **unique-constraint violation is the duplicate detection**, needing no lock. A completed key replays the stored response verbatim; the same key with a different request body returns 422.

**Deeper dive:** [`docs/concepts/idempotency-keys.md`](concepts/idempotency-keys.md).

### D4. Transactional outbox (Phase 4)

The bar for including this pattern: are there side effects that must not be lost, and can it be built in under a day? Both yes — order confirmation emails, SignalR broadcasts, low-stock alerts. The failure it prevents is real and demoable: *order committed, process crashes, customer never learns their order exists.*

A `SaveChangesInterceptor` writes domain events to `outbox_messages` **in the same transaction** as the state change; a background job claims batches with `FOR UPDATE SKIP LOCKED`, with exponential backoff and a dead-letter after five attempts.

**Cost:** ~150 lines plus a polling job, and at-least-once delivery means handlers must be idempotent.

**Deeper dive:** [`docs/concepts/transactional-outbox.md`](concepts/transactional-outbox.md).

### D5. Order state machine `[L-20]`

`Order` has private setters; the only mutation path is `TransitionTo(status, actor, reason)`, with legal transitions in a `FrozenSet` that **branches on fulfilment type** (pickup and delivery have genuinely different lifecycles). Illegal transitions return `Result.Failure`, never an exception and never a silent no-op. Every transition writes `order_status_history`.

In the legacy app the admin "process order" screen was a copy-paste of the customer cart page, so **no order could ever be marked shipped** — and since shipping was the only thing that decremented stock, inventory was never decremented at all. A typed transition matrix with a `[Theory]` over legal and illegal pairs turns that from an invisible gap into a failing test.

**Deeper dive:** [`docs/concepts/state-machine-pattern.md`](concepts/state-machine-pattern.md).

### D6. Server-side cart in Postgres, not Redis or `localStorage`

The legacy cart lived in `localStorage`, so it had no server-side existence and could not participate in a transaction with order creation `[L-15]`. Redis was rejected as the source of truth because an eviction or restart silently loses a customer's cart — "I used Redis for the cart" is a common answer with a data-loss failure mode. Postgres holds the truth; Redis caches only the derived header-badge summary.

**Deeper dive:** [`docs/concepts/cart-storage-strategy.md`](concepts/cart-storage-strategy.md).

---

## E. Security and authentication

### E1. ASP.NET Core Identity over a custom implementation

**Options:** hand-rolled users and password hashing · ASP.NET Core Identity · an external provider (Auth0, Entra ID).

Identity brings a vetted password hasher, lockout, security stamps, and email-confirmation token providers. Rolling your own spends a week to arrive somewhere worse — the legacy app is the evidence: a missing `return` in a pre-save hook re-hashed passwords on every save, breaking password reset and able to lock accounts out permanently `[L-08]`.

An external provider was rejected because auth *is* part of what this project needs to demonstrate; outsourcing it removes the interesting part.

**Chosen:** Identity for credential storage, with the **token layer built by hand** — that is where the actual engineering signal is (E2).

**Deeper dive:** [`docs/concepts/authentication-framework-choice.md`](concepts/authentication-framework-choice.md).

### E2. In-memory access token + opaque rotating refresh cookie

**Options:** JWT in `localStorage` · JWT in an `HttpOnly` cookie the API reads · short access token in memory + opaque refresh token in a scoped cookie.

`localStorage` is readable by any XSS. A cookie the API reads for authentication is CSRF-exposed. The legacy app managed to combine the weaknesses: `httpOnly` was set, but the JWT was *also* returned in the response body, no `Secure` or `SameSite` flags were set, and logout was a `GET` — so an `<img>` tag on any site could log a user out `[L-10]`.

**Chosen:** access token as a 15-minute JWT held **in memory in Redux only**; refresh token opaque, 256-bit, **stored hashed** server-side, delivered as `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`.

With **rotation and reuse detection**: every refresh issues a new token and marks the old one replaced. Presenting an already-rotated token means it leaked, so **the entire token family is revoked** and a security event is logged. ~80 lines, and it is the single most interview-relevant piece of Phase 1.

**Cost:** a hard refresh in the browser loses the in-memory access token, requiring a silent refresh round-trip on load.

**Deeper dive:** [`docs/concepts/jwt-refresh-token-strategy.md`](concepts/jwt-refresh-token-strategy.md).

### E3. Resource-based authorization over route middleware

Route-level middleware is authorization enforced by *remembering to add it* — and the legacy app proves it will eventually be forgotten. Every admin route was correctly gated except one: `DELETE /reviews` shipped with authentication but no ownership check, so any logged-in user could delete anyone's review `[L-05]`. Meanwhile all nine frontend admin guards were inert because the prop was passed to the wrong component `[L-06]`.

**Chosen:** resource-based `IAuthorizationHandler`s (`OrderOwnerOrStoreStaff`, `ReviewAuthorOrStoreStaff`) so the check is a property of the resource, backed by database constraints, and verified by a `[Theory]` that iterates **every** protected endpoint asserting 403 for a plain customer. A newly added unguarded route fails CI. Client-side guards are cosmetic by design.

Cross-tenant reads return **404, not 403**, so the API never confirms that another store's record exists.

**Deeper dive:** [`docs/concepts/authorization-strategy.md`](concepts/authorization-strategy.md).

---

## F. Infrastructure

### F1. PostgreSQL full-text search over Elasticsearch — ADR-0009 (Phase 2)

The catalogue is 100–2,000 SKUs. Elasticsearch adds a container, a memory floor, an index-sync pipeline and a consistency problem, for a corpus Postgres answers in under 5 ms. **"I added Elasticsearch to search 500 rows" reads as poor judgment, not experience.**

A generated `tsvector` column with a GIN index, queried through `EF.Functions.WebSearchToTsQuery` — a *parser*, so hostile input becomes search terms rather than syntax, closing the ReDoS and regex-injection hole `[L-13]`. A `pg_trgm` similarity fallback handles typos, so "amool" finds "Amul".

**Cost:** no distributed scaling path without a later migration; fewer relevance-tuning knobs.

**Deeper dive:** [`docs/concepts/search-strategy.md`](concepts/search-strategy.md).

### F2. Object storage over base64-in-database `[L-18]`

The legacy app stored images as base64 data URIs inside MongoDB documents — ~33% payload inflation, uncacheable by any CDN, and hard against the 16 MB document ceiling. Images become `object_key` strings; bytes live in Cloudflare R2 (zero egress fees, which matters for a demo you cannot afford to have hammered), with MinIO locally exposing the identical S3 API. Admin uploads go **direct to storage via presigned PUT**, so image bytes never pass through the API.

**Deeper dive:** [`docs/concepts/image-storage-strategy.md`](concepts/image-storage-strategy.md).

### F3. Hangfire over bare hosted services — ADR-0012 (Phase 5)

Jobs survive restarts, retry with backoff, and the dashboard is a screenshot. It is also what .NET shops actually run, so it is shared vocabulary in an interview. **Cost:** its own schema and a dashboard that must be secured.

**Deeper dive:** [`docs/concepts/background-jobs-strategy.md`](concepts/background-jobs-strategy.md).

### F4. `HybridCache` over raw `IDistributedCache` (Phase 6)

Shipped in .NET 9: L1 in-process plus L2 Redis, with **built-in stampede protection** (concurrent misses collapse to one factory call) and tag-based invalidation. Using it correctly signals currency with the platform.

**Deeper dive:** [`docs/concepts/caching-strategy.md`](concepts/caching-strategy.md).

### F5. Single-database multi-tenancy with a `store_id` discriminator — ADR-0010 (Phase 2)

**Options:** single tenant (one store) · single database with a discriminator · schema-per-tenant · database-per-tenant.

Single-tenant contradicts the product vision — "help local kirana stores get online" is plural or it is nothing. Schema- and database-per-tenant are correct at scale and enormous overkill for three demo stores.

**Chosen: discriminator, built in from Phase 2**, because retrofitting costs a month and designing it in costs five evenings. Enforced by **EF Core global query filters applied by convention** to every `ITenantEntity`, so a developer who forgets a `.Where(...)` still cannot read another store's data, plus a `SaveChanges` interceptor that stamps `store_id` on insert and throws on any attempt to change it.

**Explicitly out of scope:** self-serve onboarding, per-store subdomains, branding, billing.

**Cost:** every query carries a filter; noisy-neighbour isolation is nonexistent; a bug in the filter convention is a cross-tenant leak, so the isolation test is mandatory rather than nice-to-have.

**Deeper dive:** [`docs/concepts/multi-tenancy-strategy.md`](concepts/multi-tenancy-strategy.md).

### F6. Migrations at release, not at startup — ADR-0011 (Phase 6)

`Database.Migrate()` in `Program.cs` races across instances, requires DDL permissions at runtime, and offers no rollback point. Instead CI generates an idempotent SQL script as a reviewable build artifact, deploy applies it as a step **before** the new image rolls, the runtime database user has **no DDL permission**, and `/health/ready` fails when migrations are pending — so a mismatched deploy fails its probe instead of half-working.

Migrations must be **expand/contract**: add nullable, backfill, tighten later. Never a destructive rename in one step.

**Deeper dive:** [`docs/concepts/database-migration-deployment.md`](concepts/database-migration-deployment.md).

---

## G. Testing

### G1. Testcontainers over the in-memory provider

**Options:** EF Core in-memory provider · SQLite in-memory · a shared developer database · Testcontainers.

The in-memory provider does not enforce constraints, does not support transactions properly, and does not run SQL — so it cannot test the very things this project is about (conditional updates, unique-violation-as-idempotency, `CHECK` constraints). Passing tests against it would be actively misleading. A shared database gives cross-test interference and cannot run in CI.

**Chosen:** Testcontainers with real `postgres:17-alpine`, one container per test run via a collection fixture, **Respawn** resetting between tests.

**Cost:** ~20 s startup, and Docker becomes a hard requirement for running tests.

> **A trap worth naming:** `deploy/postgres/init.sql` creates the Postgres extensions for the Compose container only. Testcontainers starts a *different* database with no init script. Extensions must therefore be created **in an EF migration**, or tests pass locally and fail in CI.

**Deeper dive:** [`docs/concepts/integration-testing-strategy.md`](concepts/integration-testing-strategy.md).

### G2. No global coverage target

Chasing a repo-wide 80% on a project this size burns thirty hours writing tests for DTO mappers. Targets are set per layer instead: **Domain ≥85%, and 100% of the pricing engine and state machine**; every Application command gets at least one happy and one failure test **driven through the real API**; the API layer is covered contract-level with authorization asserted on every protected endpoint.

Consequently there is no `Application.UnitTests` project — testing a handler against a mocked `DbContext` mostly asserts that the mock was configured correctly.

### G3. Architecture tests as executable rules

~50 lines of NetArchTest asserting that `Domain` references nothing, `Application` never sees EF Core, entities have no public setters, handlers are sealed and internal, and every `ITenantEntity` has a query filter. Cheap, and it means the architecture in Part 1 cannot quietly erode over fourteen weeks.

**Deeper dive:** [`docs/concepts/architecture-testing.md`](concepts/architecture-testing.md).

---

## H. Frontend

### H1. Vite + TypeScript + RTK Query over CRA

Create React App is deprecated and unmaintained. The legacy frontend ships **both `@material-ui/core` v4 (EOL, not React 18 compatible) and `@mui/material` v5** — two full component libraries in one bundle, which is why the project needs `--legacy-peer-deps` to install at all.

**Chosen:** Vite, TypeScript in strict mode, Redux Toolkit + RTK Query replacing ~30 hand-rolled thunks, Tailwind + shadcn/ui.

**Deeper dive:** [`docs/concepts/frontend-stack-choice.md`](concepts/frontend-stack-choice.md).

### H2. Generated API client from OpenAPI

The TypeScript client is generated from the API's OpenAPI document in CI, with a check that fails the build if the committed client is stale. End-to-end type safety from the C# handler signature to the React prop, with zero hand-written API types.

This is the permanent structural fix for `[L-12]`: the legacy server returned `{success, error}` while every client read `.message`, so **every error message in the entire application displayed `undefined`** — two mismatched string literals that nothing could catch, because nothing typed the boundary.

**Deeper dive:** [`docs/concepts/api-client-codegen.md`](concepts/api-client-codegen.md).

---

## I. Payments and hosting

### I1. Razorpay with Cash on Delivery, over Stripe — ADR-0008 (Phase 5)

Stripe India requires a registered business entity to onboard, so a working demo is likely unobtainable; Razorpay issues test keys to an individual immediately, and the domain is Indian throughout (₹, GST, pincodes, kirana stores).

**COD is included as a first-class method** — a kirana app without cash on delivery is not credible in the Indian market. It is ~30 lines and demonstrates domain judgment.

**The webhook is the source of truth, never the client** `[L-02]`. Signature verification runs over the **raw request body** (deserializing and re-serializing breaks the signature — the single most common webhook bug), events are recorded in `payment_events` keyed on the provider event id so replays are no-ops, and a daily reconciliation job diffs the provider's payment list against ours.

**Deeper dive:** [`docs/concepts/payment-gateway-choice.md`](concepts/payment-gateway-choice.md).

### I2. Free-tier hosting, composed to hide the cold start

Render's free tier sleeps after 15 minutes, and its free Postgres **expires after 90 days** — which would silently kill the demo months later, right when someone clicks the link.

**Chosen composition:** SPA on **Cloudflare Pages** (no cold start — the link always feels instant), Postgres on **Neon** (no 90-day expiry), Redis on **Upstash**, images on **Cloudflare R2**, API on Render free Docker with a cron ping keeping it warm. The SPA renders its shell from the CDN immediately and shows a "waking the demo server" state, so a cold start reads as loading rather than broken.

Azure Container Apps with committed Bicep remains the documented production target — the strongest platform signal for a C# employer — if budget ever allows.

**Deeper dive:** [`docs/concepts/hosting-strategy.md`](concepts/hosting-strategy.md).

---

# Part 2 — Phase roadmap

~14 weeks at ~12 h/week ≈ 170 hours. Scope beyond that lives in the cut list.

```
P0 ──► P1 (identity, Result, CI, test harness)
         └─► P2 (catalogue + TENANCY + storage + search)   ← tenancy lands here or never
               ├─► P3 (SPA + OpenAPI codegen)
               └─► P4 (pricing → reservation → idempotency → outbox → state machine)
                     └─► P5 (payments ⟂ admin ⟂ SignalR)
                           └─► P6 (perf, observability, e2e, deploy, docs)
```

### Phase 0 — Foundation · Week 1 · ✅ **Complete**

Legacy app frozen into `legacy-node/` and tagged `v1-legacy-node`; [`docs/legacy-audit.md`](legacy-audit.md) written; ADRs 0000/0001/0002/0013; `docker-compose.yml` with `core`/`full` profiles; `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`.

**Done:** infrastructure healthy from a clean clone; the frozen app still boots with zero source edits.

### Phase 1 — .NET skeleton, identity, CI · Weeks 2–4 · **blocks everything**

Four source projects plus three test projects. EF Core + Npgsql with snake_case and UUIDv7, audit interceptor, first migration. ASP.NET Core Identity, JWT, **refresh rotation with reuse detection**, email verification. `IEndpoint` convention, `Result<T>` → ProblemDetails, global exception handler, correlation id. Hand-rolled dispatcher plus Scrutor and the three behaviours. Serilog, health checks, OpenAPI + Scalar. Testcontainers + Respawn harness, NetArchTest suite, green CI.

**Done:** register → verify → login → refresh → **rotate → replay old token → whole family revoked** all pass as integration tests, in CI, on a fresh container.

### Phase 2 — Catalogue, tenancy, images, search · Weeks 4–6

Store, Category, Product, ProductVariant (units, pack size, loose goods), Inventory, StockLedger. **Multi-tenancy now** — `ICurrentStore`, global query filters, stamping interceptor, isolation test. Object storage with presigned uploads and ImageSharp renditions. Postgres FTS + `pg_trgm`, facets, keyset pagination. Admin catalogue CRUD. `GroceryEasy.Migrator` (idempotent, content-addressed images) plus a grocery seeder: 3 stores, ~120 real Indian SKUs.

**Done:** a browsable, searchable, correctly-priced grocery catalogue; the store-A-reads-store-B test returns 404.

### Phase 3 — SPA foundation and catalogue parity · Weeks 6–8

Vite + React + TS strict, Tailwind, shadcn/ui, RTK Query, **OpenAPI codegen wired into CI**. Auth slice with in-memory access token and silent refresh on 401. Home, category browse, search with facets, product detail with variant/unit picker and loose-goods stepper, auth screens, profile, addresses. ProblemDetails → toast normaliser.

**Done:** the new SPA reaches catalogue and auth parity; legacy still up.

### Phase 4 — Cart, fulfilment, order placement · Weeks 8–10 · **the spine**

Server-side cart with anonymous→user merge. **Pickup vs delivery**: store hours, slot generation, capacity booking, pincode serviceability, differential pricing, the cart comparison UI. **Pricing engine** (TDD — it is pure). **Inventory reservation** and release job. **Idempotency**, **outbox**, **order state machine**. Checkout UI and order detail with timeline.

**Done:** real pickup and delivery orders placed end to end with COD; the 20-way concurrency test green; **public URL cut over to the new SPA**, `render.yaml` and `Procfile` deleted.

> Nothing here parallelises: pricing → reservation → order → idempotency/outbox is strictly sequential.

### Phase 5 — Payments, admin console, real-time · Weeks 10–12

Razorpay end to end with raw-body webhook verification, refunds and reconciliation, plus `FakePaymentGateway`. Admin console: orders Kanban, status transitions with reason capture, **pickup-code verification**, product CRUD, inventory adjustments. SignalR `OrderHub`. Hangfire dashboard and jobs.

**Done:** the full hero-GIF flow works end to end in one sitting. *(These three workstreams are parallelisable.)*

### Phase 6 — Hardening, docs, deploy · Weeks 12–14

HybridCache with tag invalidation, rate limiting, index review with `EXPLAIN ANALYZE`, N+1 sweep. OpenTelemetry, Aspire dashboard, business metrics. Bulk CSV import. Three Playwright specs. Dockerfiles, image publishing, deploy, migrations-at-release. **README with hero GIF, C4 diagram, and all ADRs.**

**Done:** a stranger clones the repo, runs two commands, and it works; the live demo is up with seeded data.

### Cut list — cut from the top when time runs short

1. Batch/expiry FEFO tracking
2. OTP/phone login
3. Bulk CSV import *(best of these three — cut 1 and 2 first to save it)*
4. Extra Hangfire jobs beyond reservation expiry, outbox and slot generation
5. Prometheus `/metrics`
6. Third store and reviews moderation UI
7. Payment reconciliation, then refunds
8. Playwright down to one spec
9. Azure Bicep

**Never cut — these *are* the project:** server-authoritative pricing · inventory reservation and the concurrency test · idempotency · pickup-vs-delivery · multi-tenancy and its isolation test · the ProblemDetails contract · refresh-token rotation · README with the audit table and ADRs · `docker compose up` working from a clean clone.

> **The trap:** breadth (ten half-built features) instead of depth (five built to production standard, with tests that prove it). A reviewer scans for one thing — *does this person's code behave correctly under concurrency, failure, and hostile input?* Phase 4 answers that. Everything else is context around it.

---

## Decision index

| Area | Decision | Chosen over | ADR |
|---|---|---|---|
| Platform | ASP.NET Core (C#) | Node/TypeScript, hybrid, Python, Rust, Go, Java | — |
| Platform | .NET 10 LTS | .NET 9 STS | [0013](adr/0013-target-dotnet-10-lts.md) |
| Data | PostgreSQL + EF Core | MongoDB, split store | [0002](adr/0002-postgresql-over-mongodb.md) |
| Migration | Clean-room rewrite | Strangler-fig, in-place refactor | [0001](adr/0001-clean-room-rewrite-over-strangler-fig.md) |
| Migration | Freeze and document defects | Patch them first | [0001](adr/0001-clean-room-rewrite-over-strangler-fig.md) |
| Architecture | Clean Architecture | N-tier, pure slices, modular monolith | 0003 |
| Architecture | Vertical slices inside layers | Grouping by technical type | 0003 |
| API | Minimal APIs + `IEndpoint` | MVC controllers, FastEndpoints | 0014 |
| Dependencies | No MediatR / AutoMapper / FluentAssertions | Paid or stale-pinned versions | 0004 |
| Errors | `Result<T>` + ProblemDetails | Exceptions for control flow | 0005 |
| Data access | Three narrow repositories | Generic `IRepository<T>` | 0006 |
| Concurrency | Conditional `UPDATE` + reservations | `FOR UPDATE`, pure optimistic | 0007 |
| Auth | Identity + hand-built token layer | Custom auth, external provider | — |
| Auth | In-memory access + rotating refresh cookie | `localStorage`, API-read cookie | — |
| Search | Postgres FTS + `pg_trgm` | Elasticsearch, Meilisearch | 0009 |
| Tenancy | Single DB + `store_id` | Single tenant, schema/DB per tenant | 0010 |
| Migrations | Applied at release | `Database.Migrate()` at startup | 0011 |
| Background | Hangfire | Bare `IHostedService` | 0012 |
| Testing | Testcontainers | In-memory provider, shared DB | — |
| Payments | Razorpay + COD | Stripe | 0008 |
| Frontend | Vite + TS + RTK Query | Create React App | — |
